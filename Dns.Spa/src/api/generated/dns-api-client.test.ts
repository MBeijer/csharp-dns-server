import { beforeEach, describe, expect, it, vi } from "vitest";
import { DnsApiClient } from "./dns-api-client";

describe("DnsApiClient", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("normalizes quoted login token", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      text: async () => "\"test-token\""
    });
    vi.stubGlobal("fetch", fetchMock);

    const client = new DnsApiClient("http://api.local");
    const token = await client.login("user", "pass");

    expect(token).toBe("test-token");
  });

  it("sends authorization header after token is set", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      json: async () => []
    });
    vi.stubGlobal("fetch", fetchMock);

    const client = new DnsApiClient("http://api.local");
    client.setBearerToken("abc123");
    await client.getZones();

    const call = fetchMock.mock.calls[0];
    const init = call[1] as RequestInit;
    const headers = init.headers as Headers;
    expect(headers.get("Authorization")).toBe("Bearer abc123");
  });

  it("posts multipart form data for bind upload imports", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true });
    vi.stubGlobal("fetch", fetchMock);

    const client = new DnsApiClient("http://api.local");
    const file = new File(["$ORIGIN example.com."], "example.zone", { type: "text/plain" });

    await client.importBindZoneFile({
      file,
      zoneSuffix: "example.com",
      enabled: true,
      replaceExistingRecords: false
    });

    const call = fetchMock.mock.calls[0];
    const init = call[1] as RequestInit;
    const body = init.body as FormData;
    expect(body.get("zoneSuffix")).toBe("example.com");
    expect(body.get("replaceExistingRecords")).toBe("false");
  });

  it("throws on existing-zone bind import when API fails", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: false, status: 500 });
    vi.stubGlobal("fetch", fetchMock);

    const client = new DnsApiClient("http://api.local");
    const file = new File(["$ORIGIN example.com."], "example.zone", { type: "text/plain" });

    await expect(
      client.importBindZoneIntoExistingZone({
        zoneId: 5,
        file,
        replaceExistingRecords: true
      })
    ).rejects.toThrow("Failed to import BIND zone into existing zone");
  });
});
