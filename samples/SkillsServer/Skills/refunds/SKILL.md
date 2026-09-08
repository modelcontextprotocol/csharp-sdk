---
name: refunds
description: Process customer refund requests per company policy.
metadata:
  owner: billing-team
  version: 2.1.0
---

# Refunds

Use this skill when a customer asks for a refund.

1. Confirm the order is within the 30-day refund window.
2. Check whether the item is refundable under `policy/REFUND_POLICY.md`.
3. If it is, issue the refund and reply using `examples/approved.md`.
4. If it is not, reply using `examples/declined.md` and offer store credit where the policy allows it.

Never issue a refund above 500 USD without a second approval.
