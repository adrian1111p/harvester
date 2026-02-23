import argparse
import threading
import time
from dataclasses import dataclass, field

from ibapi.client import EClient
from ibapi.contract import Contract
from ibapi.wrapper import EWrapper


@dataclass
class BaselineState:
    connected: bool = False
    next_valid_id: int | None = None
    managed_accounts: str = ""
    current_time_received: bool = False
    account_summary_count: int = 0
    positions_count: int = 0
    open_orders_count: int = 0
    l1_ticks: int = 0
    l2_updates: int = 0
    errors: list[tuple[int, int, str]] = field(default_factory=list)


class BaselineApp(EWrapper, EClient):
    def __init__(self, symbol: str, depth_rows: int):
        EClient.__init__(self, self)
        self.state = BaselineState()
        self.symbol = symbol
        self.depth_rows = depth_rows
        self.done = threading.Event()

    def error(self, reqId, errorCode, errorString, advancedOrderRejectJson=""):
        self.state.errors.append((reqId, errorCode, errorString))
        print(f"[ERROR] reqId={reqId} code={errorCode} msg={errorString}")

    def nextValidId(self, orderId: int):
        self.state.connected = True
        self.state.next_valid_id = orderId
        print(f"[OK] nextValidId={orderId}")

    def managedAccounts(self, accountsList: str):
        self.state.managed_accounts = accountsList
        print(f"[OK] managedAccounts={accountsList}")

    def currentTime(self, time_: int):
        self.state.current_time_received = True
        print(f"[OK] currentTime={time_}")

    def accountSummary(self, reqId, account, tag, value, currency):
        self.state.account_summary_count += 1
        if self.state.account_summary_count <= 12:
            print(f"[ACCOUNT] {account} {tag}={value} {currency}")

    def accountSummaryEnd(self, reqId):
        print(f"[OK] accountSummaryEnd reqId={reqId}")

    def position(self, account, contract, position, avgCost):
        self.state.positions_count += 1
        print(
            f"[POSITION] account={account} symbol={contract.symbol} secType={contract.secType} "
            f"qty={position} avgCost={avgCost}"
        )

    def positionEnd(self):
        print("[OK] positionEnd")

    def openOrder(self, orderId, contract, order, orderState):
        self.state.open_orders_count += 1
        print(
            f"[OPEN_ORDER] id={orderId} symbol={contract.symbol} action={order.action} "
            f"type={order.orderType} totalQty={order.totalQuantity} status={orderState.status}"
        )

    def openOrderEnd(self):
        print("[OK] openOrderEnd")

    def tickPrice(self, reqId, tickType, price, attrib):
        self.state.l1_ticks += 1
        if self.state.l1_ticks <= 20:
            print(f"[L1] reqId={reqId} tickType={tickType} price={price}")

    def tickSize(self, reqId, tickType, size):
        self.state.l1_ticks += 1
        if self.state.l1_ticks <= 20:
            print(f"[L1] reqId={reqId} tickType={tickType} size={size}")

    def updateMktDepth(
        self,
        reqId,
        position,
        operation,
        side,
        price,
        size,
    ):
        self.state.l2_updates += 1
        if self.state.l2_updates <= 40:
            print(
                f"[L2] reqId={reqId} pos={position} op={operation} side={side} "
                f"price={price} size={size}"
            )

    def updateMktDepthL2(
        self,
        reqId,
        position,
        marketMaker,
        operation,
        side,
        price,
        size,
        isSmartDepth,
    ):
        self.state.l2_updates += 1
        if self.state.l2_updates <= 40:
            print(
                f"[L2] reqId={reqId} pos={position} mm={marketMaker} op={operation} "
                f"side={side} price={price} size={size} smart={isSmartDepth}"
            )


def us_stock_contract(symbol: str, exchange: str = "SMART", primary_exchange: str = "NASDAQ"):
    contract = Contract()
    contract.symbol = symbol
    contract.secType = "STK"
    contract.currency = "USD"
    contract.exchange = exchange
    contract.primaryExchange = primary_exchange
    return contract


def run_baseline(host: str, port: int, client_id: int, symbol: str, depth_rows: int):
    app = BaselineApp(symbol=symbol, depth_rows=depth_rows)
    app.connect(host, port, client_id)

    thread = threading.Thread(target=app.run, daemon=True)
    thread.start()

    start = time.time()
    while time.time() - start < 8 and not app.state.connected:
        time.sleep(0.2)

    if not app.state.connected:
        print("[FAIL] Did not receive nextValidId. Check API settings/port/clientId.")
        app.disconnect()
        return 2

    app.reqCurrentTime()
    app.reqManagedAccts()
    app.reqAccountSummary(9001, "All", "AccountType,NetLiquidation,TotalCashValue,BuyingPower")
    app.reqPositions()
    app.reqOpenOrders()
    app.reqMarketDataType(3)

    l1_contract = us_stock_contract(symbol, exchange="SMART", primary_exchange="NASDAQ")
    app.reqMktData(1001, l1_contract, "", False, False, [])

    l2_contract = us_stock_contract(symbol, exchange="NASDAQ", primary_exchange="NASDAQ")
    app.reqMktDepth(2001, l2_contract, depth_rows, False, [])

    print("[INFO] Collecting callbacks for 20 seconds...")
    time.sleep(20)

    app.cancelMktData(1001)
    app.cancelMktDepth(2001, False)
    app.cancelAccountSummary(9001)

    try:
        app.cancelPositions()
    except Exception:
        pass

    app.disconnect()
    time.sleep(1)

    print("\n=== Baseline Summary ===")
    print(f"connected={app.state.connected}")
    print(f"nextValidId={app.state.next_valid_id}")
    print(f"managedAccounts={app.state.managed_accounts}")
    print(f"currentTimeReceived={app.state.current_time_received}")
    print(f"accountSummaryRows={app.state.account_summary_count}")
    print(f"positionsRows={app.state.positions_count}")
    print(f"openOrdersRows={app.state.open_orders_count}")
    print(f"l1Ticks={app.state.l1_ticks}")
    print(f"l2Updates={app.state.l2_updates}")
    print(f"errors={len(app.state.errors)}")

    non_connection_errors = [e for e in app.state.errors if e[1] not in (2104, 2106, 2158)]
    if non_connection_errors:
        return 1
    return 0


def main():
    parser = argparse.ArgumentParser(description="IBKR API baseline diagnostics (read-only)")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=7496)
    parser.add_argument("--client-id", type=int, default=9001)
    parser.add_argument("--symbol", default="SIRI", help="NASDAQ symbol for L1/L2 checks")
    parser.add_argument("--depth-rows", type=int, default=5)
    args = parser.parse_args()

    exit_code = run_baseline(
        host=args.host,
        port=args.port,
        client_id=args.client_id,
        symbol=args.symbol,
        depth_rows=args.depth_rows,
    )
    raise SystemExit(exit_code)


if __name__ == "__main__":
    main()
