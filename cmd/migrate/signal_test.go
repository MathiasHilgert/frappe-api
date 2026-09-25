package main

import (
	"context"
	"syscall"
	"testing"
	"time"
)

func TestSignalContextIsCanceledOnSIGTERM(t *testing.T) {
	ctx, stop := signalContext(context.Background())
	defer stop()

	if err := syscall.Kill(syscall.Getpid(), syscall.SIGTERM); err != nil {
		t.Fatalf("send SIGTERM to this process: %v", err)
	}

	select {
	case <-ctx.Done():
	case <-time.After(5 * time.Second):
		t.Fatal("signalContext's ctx was not canceled after SIGTERM")
	}
}
