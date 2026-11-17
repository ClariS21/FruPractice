import hashlib
import json
import os
import re
import time
from pathlib import Path
from urllib import error, parse, request


USER_AGENT = "fru-practice-data-script/1.0"
RATE_QUERY = "{ rateLimitData { pointsResetIn } }"


def read_credentials(path: str) -> tuple[str, str]:
    content = Path(path).read_text(encoding="utf-8")
    client_id = re.search(r"\b[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}\b", content)
    secret = re.search(r"(?<![A-Za-z0-9])[A-Za-z0-9]{40}(?![A-Za-z0-9])", content)
    if not client_id or not secret:
        raise ValueError("Credentials file is missing a client id or secret")
    return client_id.group(), secret.group()


def cache_key(query: str, variables: dict) -> str:
    return hashlib.sha1((query + json.dumps(variables, sort_keys=True)).encode()).hexdigest()


class Client:
    def __init__(self, creds_path: str, cache_dir: str):
        self.creds_path = creds_path
        self.cache_dir = Path(cache_dir)
        self._token = None

    def _post(self, url, payload, headers):
        req = request.Request(url, data=payload, headers={"User-Agent": USER_AGENT, **headers}, method="POST")
        with request.urlopen(req, timeout=90) as response:
            return json.load(response)

    def _access_token(self):
        if self._token is None:
            client_id, secret = read_credentials(self.creds_path)
            payload = parse.urlencode({"grant_type": "client_credentials", "client_id": client_id, "client_secret": secret}).encode()
            self._token = self._post("https://www.fflogs.com/oauth/token", payload,
                                     {"Content-Type": "application/x-www-form-urlencoded"})["access_token"]
        return self._token

    def _query(self, query, variables):
        payload = json.dumps({"query": query, "variables": variables}).encode()
        return self._post("https://www.fflogs.com/api/v2/client", payload,
                          {"Content-Type": "application/json", "Authorization": "Bearer " + self._access_token()})

    def gql(self, query: str, variables: dict | None = None) -> dict:
        variables = variables or {}
        path = self.cache_dir / (cache_key(query, variables) + ".json")
        if path.exists():
            return json.loads(path.read_text(encoding="utf-8"))["data"]
        rate_retries = 0
        http_retries = 0
        while True:
            try:
                result = self._query(query, variables)
                if result.get("errors"):
                    if not any("rate limit" in str(e.get("message", "")).lower() for e in result["errors"]):
                        message = str(result["errors"][0].get("message", "unknown error"))
                        if self._token:
                            message = message.replace(self._token, "[redacted]")
                        try:
                            client_id, secret = read_credentials(self.creds_path)
                            message = message.replace(client_id, "[redacted]").replace(secret, "[redacted]")
                        except (OSError, ValueError):
                            pass
                        raise ValueError("GraphQL query failed: " + message[:200])
                    limited = True
                else:
                    limited = False
            except (error.HTTPError, error.URLError) as exc:
                if isinstance(exc, error.HTTPError) and exc.code == 429:
                    limited = True
                else:
                    if http_retries >= 3:
                        raise
                    time.sleep((5, 15, 45)[http_retries])
                    http_retries += 1
                    continue
            if limited:
                if rate_retries >= 10:
                    raise RuntimeError("FFLogs rate limit retries exhausted")
                reset = self._query(RATE_QUERY, {})["data"]["rateLimitData"]["pointsResetIn"]
                time.sleep(reset + 5)
                rate_retries += 1
                continue
            self.cache_dir.mkdir(parents=True, exist_ok=True)
            tmp = Path(str(path) + ".tmp")
            tmp.write_text(json.dumps(result), encoding="utf-8")
            os.replace(tmp, path)
            return result["data"]
