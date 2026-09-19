package fixtures.orders;

/** Another module's public API, which use cases may call for an unavoidable synchronous read. */
public interface OrdersApi {

    boolean hasOpenOrders(String table);
}
