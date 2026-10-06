import importlib.util
import os
from pathlib import Path
import tempfile
import unittest


SERVER_DIR = Path(__file__).resolve().parents[1]
os.environ["FORESITE_API_TOKEN"] = "test-session-token"
os.environ["FORESITE_DATABASE_PATH"] = str(Path(tempfile.gettempdir()) / "foresite-security-test.db")
os.environ["FORESITE_CONFIG_PATH"] = str(SERVER_DIR / "config.json")

spec = importlib.util.spec_from_file_location("epyfla_server_under_test", SERVER_DIR / "epyflaServer.py")
server = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(server)


class LocalApiSecurityTests(unittest.TestCase):
    def setUp(self):
        self.client = server.app.test_client()

    def test_missing_token_is_rejected(self):
        self.assertEqual(403, self.client.get("/health").status_code)

    def test_wrong_token_is_rejected(self):
        response = self.client.get("/health", headers={"X-ForeSITE-Token": "wrong"})
        self.assertEqual(403, response.status_code)

    def test_correct_token_is_accepted(self):
        response = self.client.get(
            "/health", headers={"X-ForeSITE-Token": "test-session-token"}
        )
        self.assertEqual(200, response.status_code)


if __name__ == "__main__":
    unittest.main()
