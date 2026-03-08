import { configureStore } from "@reduxjs/toolkit";
import { beforeEach, describe, expect, it, vi } from "vitest";
import zonesReducer, { fetchZones, importBindZone, importBindZoneIntoZone } from "./zonesSlice";
import { apiClient } from "../../api/client";

vi.mock("../../api/client", () => ({
  apiClient: {
    getZones: vi.fn(),
    createZone: vi.fn(),
    updateZone: vi.fn(),
    deleteZone: vi.fn(),
    importBindZoneFile: vi.fn(),
    importBindZoneIntoExistingZone: vi.fn()
  }
}));

describe("zonesSlice", () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it("fetchZones populates items", async () => {
    vi.mocked(apiClient.getZones).mockResolvedValue([
      { id: 1, suffix: "example.com", records: [] }
    ]);

    const store = configureStore({ reducer: { zones: zonesReducer } });
    await store.dispatch(fetchZones());

    const state = store.getState().zones;
    expect(state.items).toHaveLength(1);
    expect(state.items[0]?.suffix).toBe("example.com");
    expect(state.loading).toBe(false);
  });

  it("importBindZone calls API and refreshes zones", async () => {
    vi.mocked(apiClient.importBindZoneFile).mockResolvedValue(undefined);
    vi.mocked(apiClient.getZones).mockResolvedValue([]);

    const store = configureStore({ reducer: { zones: zonesReducer } });
    const file = new File(["zone"], "example.zone", { type: "text/plain" });
    await store.dispatch(
      importBindZone({
        file,
        zoneSuffix: "example.com",
        enabled: true,
        replaceExistingRecords: true
      })
    );

    expect(apiClient.importBindZoneFile).toHaveBeenCalledTimes(1);
    expect(apiClient.getZones).toHaveBeenCalledTimes(1);
  });

  it("importBindZoneIntoZone calls API and refreshes zones", async () => {
    vi.mocked(apiClient.importBindZoneIntoExistingZone).mockResolvedValue(undefined);
    vi.mocked(apiClient.getZones).mockResolvedValue([]);

    const store = configureStore({ reducer: { zones: zonesReducer } });
    const file = new File(["zone"], "example.zone", { type: "text/plain" });
    await store.dispatch(
      importBindZoneIntoZone({
        zoneId: 10,
        file,
        replaceExistingRecords: false
      })
    );

    expect(apiClient.importBindZoneIntoExistingZone).toHaveBeenCalledTimes(1);
    expect(apiClient.getZones).toHaveBeenCalledTimes(1);
  });
});
