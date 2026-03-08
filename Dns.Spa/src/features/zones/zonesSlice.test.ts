import { configureStore } from "@reduxjs/toolkit";
import { beforeEach, describe, expect, it, vi } from "vitest";
import zonesReducer, { deleteZone, fetchZones, importBindZone, importBindZoneIntoZone, saveZone } from "./zonesSlice";
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

  it("saveZone creates when id is missing and updates when id exists", async () => {
    vi.mocked(apiClient.createZone).mockResolvedValue(undefined);
    vi.mocked(apiClient.updateZone).mockResolvedValue(undefined);
    vi.mocked(apiClient.getZones).mockResolvedValue([]);

    const store = configureStore({ reducer: { zones: zonesReducer } });

    await store.dispatch(saveZone({ suffix: "new-zone", records: [] }));
    await store.dispatch(saveZone({ id: 2, suffix: "existing-zone", records: [] }));

    expect(apiClient.createZone).toHaveBeenCalledTimes(1);
    expect(apiClient.updateZone).toHaveBeenCalledTimes(1);
  });

  it("deleteZone calls API and refreshes zones", async () => {
    vi.mocked(apiClient.deleteZone).mockResolvedValue(undefined);
    vi.mocked(apiClient.getZones).mockResolvedValue([]);

    const store = configureStore({ reducer: { zones: zonesReducer } });
    await store.dispatch(deleteZone(4));

    expect(apiClient.deleteZone).toHaveBeenCalledWith(4);
    expect(apiClient.getZones).toHaveBeenCalledTimes(1);
  });

  it("sets error on rejected async actions", async () => {
    vi.mocked(apiClient.getZones).mockRejectedValue(new Error("fetch-fail"));
    vi.mocked(apiClient.importBindZoneIntoExistingZone).mockRejectedValue(new Error("import-fail"));

    const store = configureStore({ reducer: { zones: zonesReducer } });
    await store.dispatch(fetchZones());

    let state = store.getState().zones;
    expect(state.error).toContain("fetch-fail");

    const file = new File(["zone"], "example.zone", { type: "text/plain" });
    await store.dispatch(importBindZoneIntoZone({ zoneId: 10, file, replaceExistingRecords: true }));

    state = store.getState().zones;
    expect(state.error).toContain("import-fail");
  });

  it("sets specific errors for save/delete/importBind failures", async () => {
    const store = configureStore({ reducer: { zones: zonesReducer } });

    vi.mocked(apiClient.createZone).mockRejectedValueOnce(new Error("create-fail"));
    await store.dispatch(saveZone({ suffix: "new-zone", records: [] }));
    expect(store.getState().zones.error).toContain("create-fail");

    vi.mocked(apiClient.deleteZone).mockRejectedValueOnce(new Error("delete-fail"));
    await store.dispatch(deleteZone(9));
    expect(store.getState().zones.error).toContain("delete-fail");

    vi.mocked(apiClient.importBindZoneFile).mockRejectedValueOnce(new Error("bind-fail"));
    const file = new File(["zone"], "example.zone", { type: "text/plain" });
    await store.dispatch(importBindZone({ file, zoneSuffix: "example.com", enabled: true, replaceExistingRecords: true }));
    expect(store.getState().zones.error).toContain("bind-fail");
  });
});
