import requests

BASE_URL = "http://localhost:5000"
session = requests.Session()

def test_flow():
    print("1. Testing Registration...")
    try:
        # Register a new test user
        r = session.post(f"{BASE_URL}/api/auth/register", data={"username": "testuser", "password": "password123"}, allow_redirects=True)
        print(f"   Registration status: {r.status_code}")
        
        print("2. Testing Login...")
        r = session.post(f"{BASE_URL}/api/auth/login", data={"username": "testuser", "password": "password123"}, allow_redirects=True)
        print(f"   Login status: {r.status_code}")
        if r.status_code != 200:
            print("   Login failed, exiting.")
            return

        print("3. Testing Chat Interaction (with empty sessionId)...")
        # Simulate HTMX post with empty sessionId string
        r = session.post(f"{BASE_URL}/api/chat", data={"content": "Hello AI!", "sessionId": ""}, allow_redirects=True)
        print(f"   Chat response status: {r.status_code}")
        if r.status_code == 200:
            print("   Chat message successful! Content received.")
        else:
            print(f"   Chat message failed. Body: {r.text}")

    except Exception as e:
        print(f"   An error occurred during testing: {e}")

if __name__ == "__main__":
    test_flow()
