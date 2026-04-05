"""
Unit tests for LangGraph flows — no live system credentials needed.
Run: pytest tests/python/ -m "not integration" -v
"""
import pytest

@pytest.mark.unit
def test_price_sync_state_keys():
    """PriceSyncState must have all required keys."""
    from src.langgraph.flows.price_sync_flow import PriceSyncState
    import typing
    hints = typing.get_type_hints(PriceSyncState)
    required = {"sap_material", "sf_product_id", "triggered_by", "audit_trail", "final_status"}
    assert required.issubset(set(hints.keys()))

@pytest.mark.unit
def test_incident_state_keys():
    from src.langgraph.flows.incident_triage_flow import IncidentTriageState
    import typing
    hints = typing.get_type_hints(IncidentTriageState)
    assert "incident_number" in hints
    assert "ai_classification" in hints

@pytest.mark.unit
def test_stripe_state_keys():
    from src.langgraph.flows.stripe_billing_flow import StripeBillingState
    import typing
    hints = typing.get_type_hints(StripeBillingState)
    assert "stripe_event_type" in hints
    assert "snow_incident_id" in hints

@pytest.mark.unit
def test_audit_helper_returns_dict():
    from src.langgraph.flows.price_sync_flow import _audit
    result = _audit("test_node", "test_action", "test_result")
    assert "audit_trail" in result
    assert len(result["audit_trail"]) == 1
    assert result["audit_trail"][0]["node"] == "test_node"

@pytest.mark.unit
def test_audit_has_timestamp():
    from src.langgraph.flows.price_sync_flow import _audit
    result = _audit("n", "a", "r")
    assert "ts" in result["audit_trail"][0]
