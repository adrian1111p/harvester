# TWS API Connection Checklist (Windows)

Use this checklist to make TWS expose the API socket before moving to orders/portfolio/market-data API calls.

## Current detected state

- TWS process is running.
- Active TWS profile file: `C:\Jts\aelfjmbilcfccnnejjhdgkgiegbgehncniooiglg\tws.xml`
- API settings already detected in file:
  - `socketClient=true`
  - `port=7496`
  - `allowOnlyLocalhost=true`
  - `readOnlyApi=true`
- API port is **not listening yet**.

## Required TWS GUI settings

In TWS, open:

`File > Global Configuration > API > Settings`

Set/verify:

1. **Enable ActiveX and Socket Clients** = checked
2. **Socket port** = `7496` (live) 
3. **Allow connections from localhost only** = checked
4. **Trusted IPs** includes `127.0.0.1`
5. Keep **Read-Only API** checked for initial validation (safe mode)

Then click **Apply** and **OK**.

## Important runtime requirement

TWS must be fully logged in (not only at login screen) for the socket listener to appear.

## Verify from repo

From workspace root:

```powershell
.\ops\verify_tws_api_connection.ps1
```

Expected READY condition:

- TWS running
- `socketClient=true`
- Port `7496` listening

## Next step once READY

After `READY`, proceed with API baseline checks in order:

1. Connection handshake (`nextValidId`, server time)
2. Account + portfolio snapshot
3. Open orders / executions
4. Market data L1
5. NASDAQ TotalView-OpenView depth (L2)
