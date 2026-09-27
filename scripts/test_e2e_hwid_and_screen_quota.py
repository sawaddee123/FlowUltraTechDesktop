import time
import requests
import json
import sys

sys.stdout.reconfigure(encoding='utf-8')

BASE_URL = "http://210.246.215.66:8001"

def print_step(title):
    print(f"\n========================================================")
    print(f"  {title}")
    print(f"========================================================")

def main():
    print_step("E2E TEST: UNLIMITED HWID (MACHINES) + STRICT SCREEN QUOTA")

    # -------------------------------------------------------------
    # 1. Admin Login to setup test user
    # -------------------------------------------------------------
    print_step("Step 1: Admin Login as Root")
    login_res = requests.post(f"{BASE_URL}/api/v1/auth/login", json={
        "username": "root",
        "password": "!WFXU#v1aO7338+",
        "hwid": "ROOT-TEST-ADMIN-HWID"
    })
    assert login_res.status_code == 200, f"Root login failed: {login_res.text}"
    admin_token = login_res.json()["accessToken"]
    admin_headers = {"Authorization": f"Bearer {admin_token}"}
    print("✔ Root login successful. Token acquired.")

    # -------------------------------------------------------------
    # 2. Create Test User with Screen Quota = 2
    # -------------------------------------------------------------
    print_step("Step 2: Create Test User with maxTiktokAccounts = 2")
    test_username = f"e2e_user_{int(time.time())}"
    test_password = "Password123!"
    create_res = requests.post(f"{BASE_URL}/api/v1/admin/users", headers=admin_headers, json={
        "username": test_username,
        "password": test_password,
        "role": "user",
        "maxTiktokAccounts": 2,
        "daysValid": 30
    })
    assert create_res.status_code == 201, f"Create user failed: {create_res.text}"
    created_user = create_res.json()["user"]
    print(f"✔ Created user '{test_username}' with maxTiktokAccounts={created_user['license']['maxTiktokAccounts']}, deviceLimit={created_user['license']['deviceLimit']}")

    # -------------------------------------------------------------
    # 3. Test Multi-Device Login (No HWID / Machine Limit)
    # -------------------------------------------------------------
    print_step("Step 3: Test Multi-Device Login (3 Different HWIDs)")
    devices = [
        {"name": "PC-Workstation-1", "hwid": "HWID-WORKSTATION-AAA"},
        {"name": "Laptop-Office", "hwid": "HWID-LAPTOP-BBB"},
        {"name": "PC-Home-Gaming", "hwid": "HWID-HOME-CCC"},
    ]
    tokens = {}

    for d in devices:
        res = requests.post(f"{BASE_URL}/api/v1/auth/login", json={
            "username": test_username,
            "password": test_password,
            "hwid": d["hwid"],
            "deviceName": d["name"]
        })
        assert res.status_code == 200, f"Login from {d['name']} failed! Status {res.status_code}: {res.text}"
        tokens[d["hwid"]] = res.json()["accessToken"]
        print(f"✔ Login SUCCESS from {d['name']} (HWID: {d['hwid']}) -> No HWID block!")

    # -------------------------------------------------------------
    # 4. Open Screen 1 on Device AAA (Profile: Acc01)
    # -------------------------------------------------------------
    print_step("Step 4: Device AAA opens Screen 1 ('Acc01')")
    h_dev1 = {"Authorization": f"Bearer {tokens['HWID-WORKSTATION-AAA']}"}
    slot1_res = requests.post(f"{BASE_URL}/api/v1/slots/acquire", headers=h_dev1, json={
        "profileFolderName": "Acc01",
        "hwid": "HWID-WORKSTATION-AAA"
    })
    assert slot1_res.status_code == 200, f"Device 1 failed to acquire slot 1: {slot1_res.text}"
    lease1 = slot1_res.json()["leaseId"]
    print(f"✔ Screen 1 ('Acc01') acquired on Device AAA: LeaseId={lease1}")

    # -------------------------------------------------------------
    # 5. Open Screen 2 on Device BBB (Profile: Acc02)
    # -------------------------------------------------------------
    print_step("Step 5: Device BBB opens Screen 2 ('Acc02')")
    h_dev2 = {"Authorization": f"Bearer {tokens['HWID-LAPTOP-BBB']}"}
    slot2_res = requests.post(f"{BASE_URL}/api/v1/slots/acquire", headers=h_dev2, json={
        "profileFolderName": "Acc02",
        "hwid": "HWID-LAPTOP-BBB"
    })
    assert slot2_res.status_code == 200, f"Device 2 failed to acquire slot 2: {slot2_res.text}"
    lease2 = slot2_res.json()["leaseId"]
    print(f"✔ Screen 2 ('Acc02') acquired on Device BBB: LeaseId={lease2}")
    print("ℹ Total Active Screens across all machines: 2 / 2 (Quota Full)")

    # -------------------------------------------------------------
    # 6. Attempt to open Screen 3 on Device CCC (Profile: Acc03) -> MUST BE BLOCKED BY SCREEN QUOTA
    # -------------------------------------------------------------
    print_step("Step 6: Device CCC attempts to open Screen 3 ('Acc03') -> Exceeds Quota")
    h_dev3 = {"Authorization": f"Bearer {tokens['HWID-HOME-CCC']}"}
    slot3_res = requests.post(f"{BASE_URL}/api/v1/slots/acquire", headers=h_dev3, json={
        "profileFolderName": "Acc03",
        "hwid": "HWID-HOME-CCC"
    })
    assert slot3_res.status_code == 403, f"Expected 403 Quota Exceeded, got {slot3_res.status_code}: {slot3_res.text}"
    err_msg = slot3_res.json().get("error", "")
    assert "เกินโควต้า" in err_msg or "โควต้า" in err_msg, f"Unexpected error message: {err_msg}"
    print(f"✔ Correctly BLOCKED: {err_msg}")

    # -------------------------------------------------------------
    # 7. Test Heartbeats for Active Leases on Device AAA & BBB
    # -------------------------------------------------------------
    print_step("Step 7: Send Heartbeat for Active Leases 1 & 2")
    hb1 = requests.post(f"{BASE_URL}/api/v1/slots/heartbeat", headers=h_dev1, json={
        "leaseId": lease1,
        "hwid": "HWID-WORKSTATION-AAA"
    })
    assert hb1.status_code == 200, f"Heartbeat for Lease 1 failed: {hb1.text}"
    print("✔ Heartbeat SUCCESS for Lease 1 (Device AAA)")

    hb2 = requests.post(f"{BASE_URL}/api/v1/slots/heartbeat", headers=h_dev2, json={
        "leaseId": lease2,
        "hwid": "HWID-LAPTOP-BBB"
    })
    assert hb2.status_code == 200, f"Heartbeat for Lease 2 failed: {hb2.text}"
    print("✔ Heartbeat SUCCESS for Lease 2 (Device BBB)")

    # -------------------------------------------------------------
    # 8. Admin Revokes Lease 1 (Device AAA stops Screen 1)
    # -------------------------------------------------------------
    print_step("Step 8: Admin Revokes Lease 1 (Screen 1 closed)")
    revoke_res = requests.delete(f"{BASE_URL}/api/v1/admin/leases/{lease1}", headers=admin_headers)
    assert revoke_res.status_code == 200, f"Revoke failed: {revoke_res.text}"
    print(f"✔ Lease 1 ('Acc01') revoked successfully. Active screens: 1 / 2")

    # -------------------------------------------------------------
    # 9. Device CCC now opens Screen 3 ('Acc03') -> MUST NOW SUCCEED
    # -------------------------------------------------------------
    print_step("Step 9: Device CCC tries opening Screen 3 ('Acc03') again")
    slot3_retry = requests.post(f"{BASE_URL}/api/v1/slots/acquire", headers=h_dev3, json={
        "profileFolderName": "Acc03",
        "hwid": "HWID-HOME-CCC"
    })
    assert slot3_retry.status_code == 200, f"Device 3 retry failed: {slot3_retry.text}"
    lease3 = slot3_retry.json()["leaseId"]
    print(f"✔ Screen 3 ('Acc03') acquired on Device CCC: LeaseId={lease3}")
    print("ℹ Total Active Screens across all machines: 2 / 2 (Acc02 on Device BBB + Acc03 on Device CCC)")

    # -------------------------------------------------------------
    # 10. Clean up test user
    # -------------------------------------------------------------
    print_step("Step 10: Cleanup Test User")
    del_res = requests.delete(f"{BASE_URL}/api/v1/admin/users/{test_username}", headers=admin_headers)
    assert del_res.status_code == 200, f"Delete test user failed: {del_res.text}"
    print(f"✔ Test user '{test_username}' cleaned up successfully.")

    print_step("🎉 ALL E2E TESTS PASSED 100% PERFECTLY!")

if __name__ == "__main__":
    main()
