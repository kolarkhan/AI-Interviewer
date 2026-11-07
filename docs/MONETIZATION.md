# Monetization

## Plans (source of truth: `backend/app/modules/billing/plans.py`)

| | Free | Premium |
|---|---|---|
| Interviews / month | 3 | 40 (fair use: 600 voice minutes) |
| Max duration | 10 min | 30 min |
| Interview types | Technical, Behavioral | All (incl. HR, Mixed) |
| Difficulty | Beginner, Intermediate | + Advanced |
| Custom role | – | ✓ |
| Resume-based questions | – | ✓ |
| Report | Basic (scores, summary, strengths, improvements, topics) | Detailed (+ per-question feedback, improved answers, delivery patterns, practice plan) |
| History / progress | last 5 / summary only | full history, trends, per-skill progress |
| Voices / avatars | 2 (marin, cedar) / Alex & Maya | 6 / Alex & Maya |
| Realtime model | `gpt-realtime-2.1-mini` | `gpt-realtime-2.1` |

Enforcement is server-side: plan checks at interview creation, monthly counters (`usage_records`), voice minutes capped at live-connect time, and duration enforced by the relay. The app only reflects the `locked` flags returned by `/v1/catalog`.

## What exists today
- Subscription document per user (`subscriptions`), plan resolution with expiry, usage summary endpoints, a paywall screen, locked-feature UX, and upgrade prompts on 402 errors.
- `POST /v1/billing/verify-purchase` returns **501** `billing_not_configured`. The in-app Upgrade button says so honestly.
- Development-only test grant (`/v1/billing/dev/grant-premium`, 404 in production), for demos and QA.

## Integrating real payments (remaining work)

1. **Store products:** one auto-renewing subscription group (e.g. `premium_monthly`, `premium_yearly`) in Google Play Console and App Store Connect.
2. **Client:** Unity IAP (`com.unity.purchasing`) or native StoreKit 2 / Play Billing Library plugins. On purchase, send the Play `purchaseToken` or App Store signed transaction to `/v1/billing/verify-purchase`. Never grant entitlements client-side.
3. **Server verification:**
   - Google: Play Developer API `purchases.subscriptionsv2.get` with a service account; acknowledge purchases within 3 days.
   - Apple: App Store Server API (signed JWS transactions; verify the certificate chain) using an in-app purchase key.
   - Store `source`, `product_id`, `store_transaction_id`, `current_period_end`, `verified=true`.
4. **Lifecycle webhooks:** Google Real-time Developer Notifications (Pub/Sub) and App Store Server Notifications V2 for renewals, grace periods, refunds and revocations. Update `subscriptions.status` and `current_period_end`.
5. **Restore purchases** button on iOS (App Store requirement) and account linking (one subscription → one account).
6. **Compliance:** clear pricing, renewal terms and cancellation instructions on the paywall, links to Terms and Privacy, and no external payment links in-app unless allowed in your region.
7. Tests: sandbox and licence testers on both stores, plus expiry, grace, refund and cross-device restore.
