package com.frappe.platform.infrastructure.valkey;

import io.github.bucket4j.redis.lettuce.RedisApi;
import io.lettuce.core.RedisFuture;
import io.lettuce.core.ScriptOutputType;
import io.lettuce.core.cluster.api.async.RedisClusterAsyncCommands;
import org.springframework.data.redis.connection.lettuce.LettuceConnectionFactory;

/**
 * Bucket4j's view of Valkey, backed by the shared native connection of Spring's {@link LettuceConnectionFactory}.
 * Bucket4j's own builders connect eagerly, which would stop startup without Valkey; this opens the connection on first
 * use, reconnects like every other Valkey call, and keeps one client (and Boot's Lettuce observation) for the API.
 *
 * <p>Generic in the key type on purpose: Bucket4j passes keys as an erased {@code K[]} (an {@code Object[]} at run
 * time), which a class fixed to {@code byte[]} keys would fail to cast. Use it with {@code byte[]} keys, the codec of
 * Spring's connection.
 *
 * @param <K> the key type, {@code byte[]}
 */
final class SharedConnectionRedisApi<K> implements RedisApi<K> {

    private final LettuceConnectionFactory connections;

    /**
     * Creates the API.
     *
     * @param connections the factory; must share its native connection (Spring Boot's default)
     * @throws IllegalStateException if the factory hands out dedicated connections, which would be released while
     *     Bucket4j still awaits their replies
     */
    SharedConnectionRedisApi(LettuceConnectionFactory connections) {
        if (!connections.getShareNativeConnection()) {
            throw new IllegalStateException("Rate limits need a shared Lettuce connection;"
                    + " keep LettuceConnectionFactory#setShareNativeConnection(true)");
        }
        this.connections = connections;
    }

    @Override
    public <V> RedisFuture<V> eval(String script, ScriptOutputType scriptOutputType, K[] keys, byte[][] params) {
        return commands().eval(script, scriptOutputType, keys, params);
    }

    @Override
    public RedisFuture<byte[]> get(K key) {
        return commands().get(key);
    }

    @Override
    public RedisFuture<?> delete(K key) {
        return commands().del(key);
    }

    @SuppressWarnings("unchecked")
    private RedisClusterAsyncCommands<K, byte[]> commands() {
        // Closing the Spring connection releases only what it opened itself; the shared native connection stays open
        // for the pending future.
        try (var connection = connections.getConnection()) {
            return (RedisClusterAsyncCommands<K, byte[]>) connection.getNativeConnection();
        }
    }
}
