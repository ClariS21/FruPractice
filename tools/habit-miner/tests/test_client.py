import json
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from urllib.error import URLError

from habitminer.client import Client, cache_key, read_credentials


class ClientTests(unittest.TestCase):
    def test_read_credentials(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d) / "creds"
            p.write_text("id: 0a1b2c3d-1111-2222-3333-444455556666\nsecret: " + "A" * 40)
            self.assertEqual(read_credentials(str(p)), ("0a1b2c3d-1111-2222-3333-444455556666", "A" * 40))

    def test_cache_key(self):
        self.assertEqual(cache_key("q", {"a": 1, "b": 2}), cache_key("q", {"b": 2, "a": 1}))
        self.assertNotEqual(cache_key("q", {"a": 1}), cache_key("q", {"a": 2}))

    def test_cached_without_network(self):
        with tempfile.TemporaryDirectory() as d:
            Path(d, cache_key("q", {"x": 1}) + ".json").write_text(json.dumps({"data": {"x": 1}}))
            self.assertEqual(Client("missing", d).gql("q", {"x": 1}), {"x": 1})

    def test_dropped_connection_retries_and_caches(self):
        with tempfile.TemporaryDirectory() as d, patch("habitminer.client.time.sleep") as sleep:
            client = Client("missing", d)
            with patch.object(client, "_query", side_effect=[URLError("offline"), {"data": {"x": 1}}]) as query:
                self.assertEqual(client.gql("q"), {"x": 1})
                self.assertEqual(query.call_count, 2)
            self.assertEqual(client.gql("q"), {"x": 1})
            sleep.assert_called_once_with(5)

    def test_graphql_error_message(self):
        with tempfile.TemporaryDirectory() as d:
            client = Client("missing", d)
            with patch.object(client, "_query", return_value={"errors": [{"message": "boom"}]}):
                with self.assertRaisesRegex(ValueError, "boom"):
                    client.gql("q")

    def test_urlopen_has_timeout(self):
        with patch("habitminer.client.request.urlopen") as open_url:
            open_url.return_value.__enter__.return_value.read.return_value = b'{"data":{}}'
            Client("missing", "unused")._post("https://example.invalid", b"{}", {})
            self.assertEqual(open_url.call_args.kwargs["timeout"], 90)
