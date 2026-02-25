import argparse
import hashlib
import hmac
import json
import time
from pathlib import Path


def now_ns() -> int:
    return time.time_ns()


def canonical_payload_json(payload: dict) -> str:
    return json.dumps(payload, sort_keys=True, separators=(",", ":"))


def hmac_sha256_hex(secret: str, text: str) -> str:
    return hmac.new(secret.encode("utf-8"), text.encode("utf-8"), hashlib.sha256).hexdigest()


def build_approval(
    approval_id: str,
    model_id: str,
    from_stage: str,
    to_stage: str,
    approved_by: str,
    change_ticket: str,
    issued_ts_ns: int,
    expires_ts_ns: int,
    team: str,
    signing_key: str | None,
) -> dict:
    payload = {
        "model_id": model_id,
        "from_stage": from_stage,
        "to_stage": to_stage,
        "approved_by": approved_by,
        "change_ticket": change_ticket,
        "issued_ts_ns": issued_ts_ns,
        "expires_ts_ns": expires_ts_ns,
        "team": team,
    }

    signature = hmac_sha256_hex(signing_key, canonical_payload_json(payload)) if signing_key else ""

    return {
        "approval_id": approval_id,
        **payload,
        "signature": signature,
    }


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Generate legacy-compatible self-learning promotion approvals JSON."
    )
    parser.add_argument(
        "--output",
        default="exports/model_promotion_approvals.json",
        help="Output approvals JSON path.",
    )
    parser.add_argument("--model-id", default="*", help="Model id or * wildcard.")
    parser.add_argument("--from-stage", default="candidate")
    parser.add_argument("--to-stage", default="shadow")
    parser.add_argument("--approved-by", default="ml-governance")
    parser.add_argument("--change-ticket", default="CHG-ML-0001")
    parser.add_argument(
        "--teams",
        default="engineering",
        help="Comma-separated team names. One approval row is generated per team.",
    )
    parser.add_argument(
        "--expires-hours",
        type=int,
        default=24,
        help="Approval expiry horizon in hours.",
    )
    parser.add_argument(
        "--signing-key",
        default="",
        help="Optional signing key. If set, signatures are written per approval row.",
    )
    args = parser.parse_args()

    output_path = Path(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    issued_ts_ns = now_ns()
    expires_ts_ns = issued_ts_ns + max(1, args.expires_hours) * 60 * 60 * 1_000_000_000
    teams = [team.strip().lower() for team in args.teams.split(",") if team.strip()]
    if not teams:
        teams = ["engineering"]

    approvals = []
    for idx, team in enumerate(teams, start=1):
        approval_id = f"appr-{issued_ts_ns}-{idx}"
        approval = build_approval(
            approval_id=approval_id,
            model_id=args.model_id,
            from_stage=args.from_stage.strip().lower(),
            to_stage=args.to_stage.strip().lower(),
            approved_by=args.approved_by,
            change_ticket=args.change_ticket,
            issued_ts_ns=issued_ts_ns,
            expires_ts_ns=expires_ts_ns,
            team=team,
            signing_key=args.signing_key.strip() or None,
        )
        approvals.append(approval)

    doc = {
        "version": 1,
        "generated_ts_ns": now_ns(),
        "approvals": approvals,
    }

    output_path.write_text(json.dumps(doc, indent=2), encoding="utf-8")

    print(f"[OK] Wrote approvals: {output_path.resolve()}")
    print(
        f"[OK] transition={args.from_stage.strip().lower()}->{args.to_stage.strip().lower()} "
        f"teams={','.join(teams)} signed={bool(args.signing_key.strip())} rows={len(approvals)}"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
