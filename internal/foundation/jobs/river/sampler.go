package river

import (
	"context"
	"fmt"
	"log/slog"
	"sync"
	"time"

	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"
)

// depthStatement counts unfinished jobs per queue and state. Finished
// states (completed, cancelled, discarded) are left out: they only grow
// until the cleaner removes them and say nothing about backlog.
const depthStatement = `
SELECT queue, state::text, count(*)
FROM river_job
WHERE state IN ('available', 'pending', 'retryable', 'running', 'scheduled')
GROUP BY queue, state`

// leaderStatement reports whether identifier holds an unexpired leadership.
const leaderStatement = `SELECT EXISTS (SELECT 1 FROM river_leader WHERE leader_id = $1 AND expires_at > now())`

// depthKey identifies one queue depth series.
type depthKey struct {
	queue string
	state string
}

// snapshot is the latest sample, read by the observable gauge callback.
type snapshot struct {
	depth  map[depthKey]int64
	leader bool
	mutex  sync.Mutex
}

func (sample *snapshot) store(depth map[depthKey]int64, leader bool) {
	sample.mutex.Lock()
	defer sample.mutex.Unlock()
	sample.depth = depth
	sample.leader = leader
}

// registerGauges registers the callback observing the latest snapshot. The
// callback never queries the database, so a slow database never stalls a
// metrics export. Queue depth is observed on the leader only.
func (client *Client) registerGauges() (metric.Registration, error) {
	instruments := client.instruments
	registration, err := instruments.meter.RegisterCallback(func(_ context.Context, observer metric.Observer) error {
		client.sample.mutex.Lock()
		defer client.sample.mutex.Unlock()
		leader := int64(0)
		if client.sample.leader {
			leader = 1
			for key, count := range client.sample.depth {
				observer.ObserveInt64(instruments.depth, count, metric.WithAttributes(
					attribute.String("queue", key.queue), attribute.String("state", key.state)))
			}
		}
		observer.ObserveInt64(instruments.leader, leader)
		return nil
	}, instruments.depth, instruments.leader)
	if err != nil {
		return nil, fmt.Errorf("river jobs: register gauges: %w", err)
	}
	return registration, nil
}

// runSampler samples queue depth and leadership every MetricsInterval until
// ctx is cancelled, then closes done.
func (client *Client) runSampler(ctx context.Context, done chan<- struct{}) {
	defer close(done)
	ticker := time.NewTicker(client.settings.MetricsInterval)
	defer ticker.Stop()
	for {
		client.sampleOnce(ctx)
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
		}
	}
}

func (client *Client) sampleOnce(ctx context.Context) {
	depth, err := client.queryDepth(ctx)
	if err != nil {
		if ctx.Err() == nil {
			slog.WarnContext(ctx, "river jobs: sample queue depth", slog.Any("error", err))
		}
		return
	}
	var leader bool
	if err := client.pool.QueryRow(ctx, leaderStatement, client.identifier).Scan(&leader); err != nil {
		if ctx.Err() == nil {
			slog.WarnContext(ctx, "river jobs: sample leadership", slog.Any("error", err))
		}
		return
	}
	client.sample.store(depth, leader)
}

func (client *Client) queryDepth(ctx context.Context) (map[depthKey]int64, error) {
	rows, err := client.pool.Query(ctx, depthStatement)
	if err != nil {
		return nil, err
	}
	defer rows.Close()
	depth := map[depthKey]int64{}
	for queue := range client.queues {
		for _, state := range []string{"available", "pending", "retryable", "running", "scheduled"} {
			depth[depthKey{queue: queue, state: state}] = 0
		}
	}
	for rows.Next() {
		var key depthKey
		var count int64
		if err := rows.Scan(&key.queue, &key.state, &count); err != nil {
			return nil, err
		}
		depth[key] = count
	}
	return depth, rows.Err()
}
