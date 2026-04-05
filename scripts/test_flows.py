#!/usr/bin/env python3
"""
test_flows.py — Automated tests for all flow logic. No credentials needed.
Usage: python3 scripts/test_flows.py
"""
import sys, json
sys.path.insert(0, '.')

PASS = "\033[92m✅\033[0m"
FAIL = "\033[91m❌\033[0m"
results = []

def test(name, fn):
    try:
        fn()
        print(f"  {PASS} {name}")
        results.append((name, True))
    except AssertionError as e:
        print(f"  {FAIL} {name}: {e}")
        results.append((name, False))
    except Exception as e:
        print(f"  {FAIL} {name}: EXCEPTION — {e}")
        results.append((name, False))

print("\n\033[1mAzentix — Flow Logic Tests\033[0m\n")

# Import demo flows
from scripts.demo_offline import (
    run_price_sync, run_incident_triage, run_stripe_billing,
    run_lead_enrichment, run_hubspot_sync
)

print("Flow 1: SAP → Salesforce Price Sync")
def t_price_sync_synced():
    r = run_price_sync("MAT-001", "SF-001")
    assert r["final_status"] == "synced", f"Expected synced, got {r['final_status']}"
def t_price_sync_has_ref():
    r = run_price_sync("MAT-002", "SF-002")
    assert r.get("sync_ref", "").startswith("SYNC-"), "sync_ref missing SYNC- prefix"
def t_price_sync_audit():
    r = run_price_sync("MAT-003", "SF-003")
    assert len(r.get("audit_trail", [])) >= 5, "Expected ≥5 audit entries"
test("price_sync returns synced", t_price_sync_synced)
test("price_sync has sync_ref", t_price_sync_has_ref)
test("price_sync audit trail ≥5 entries", t_price_sync_audit)

print("\nFlow 2: ServiceNow Incident Triage")
def t_triage_escalated():
    r = run_incident_triage("INC0001111")
    assert r["final_status"] == "escalated"
def t_triage_has_classification():
    r = run_incident_triage("INC0002222")
    assert "ai_classification" in r
def t_triage_group_assigned():
    r = run_incident_triage("INC0003333")
    assert r["ai_classification"]["assignment_group"] == "SAP-Basis"
test("incident_triage returns escalated", t_triage_escalated)
test("incident_triage has ai_classification", t_triage_has_classification)
test("incident_triage assigns correct group", t_triage_group_assigned)

print("\nFlow 3: Stripe Failed Payment")
def t_stripe_handled():
    r = run_stripe_billing("payment_intent.payment_failed", "pi_abc")
    assert r["final_status"] == "handled"
def t_stripe_creates_incident():
    r = run_stripe_billing("payment_intent.payment_failed", "pi_def")
    assert r.get("snow_incident_id") == "INC0009876"
def t_stripe_updates_all_systems():
    r = run_stripe_billing("payment_intent.payment_failed", "pi_ghi")
    assert r.get("sf_opportunity_id") is not None
    assert r.get("hubspot_contact_id") is not None
test("stripe_billing returns handled", t_stripe_handled)
test("stripe_billing creates ServiceNow incident", t_stripe_creates_incident)
test("stripe_billing updates SF + HubSpot", t_stripe_updates_all_systems)

print("\nFlow 4: Lead Enrichment")
def t_lead_enriched():
    r = run_lead_enrichment("LEAD-001")
    assert r["final_status"] == "enriched"
def t_lead_has_sap_data():
    r = run_lead_enrichment("LEAD-002")
    assert r.get("sap_customer_data", {}).get("bp_id") == "BP-UK-001234"
def t_lead_completeness():
    r = run_lead_enrichment("LEAD-003")
    assert r.get("enrichment_fields", {}).get("Azentix_Data_Completeness__c") == 95
test("lead_enrichment returns enriched", t_lead_enriched)
test("lead_enrichment has SAP BP data", t_lead_has_sap_data)
test("lead_enrichment sets completeness=95", t_lead_completeness)

print("\nFlow 5: HubSpot Sync")
def t_hubspot_synced():
    r = run_hubspot_sync("OPP-001")
    assert r["final_status"] == "synced"
def t_hubspot_contact_created():
    r = run_hubspot_sync("OPP-002")
    assert r.get("action_taken") == "created"
    assert r.get("hs_contact_id") is not None
test("hubspot_sync returns synced", t_hubspot_synced)
test("hubspot_sync creates contact", t_hubspot_contact_created)

# Summary
total = len(results)
passed = sum(1 for _, ok in results if ok)
failed = total - passed
print(f"\n{'='*45}")
print(f"Results: {passed}/{total} passed", end="  ")
if failed == 0:
    print("\033[92m ALL TESTS PASSED ✅\033[0m")
    sys.exit(0)
else:
    print(f"\033[91m{failed} FAILED ❌\033[0m")
    for name, ok in results:
        if not ok: print(f"  ❌ {name}")
    sys.exit(1)
