import { createAsyncThunk, createSlice } from "@reduxjs/toolkit";
import { apiClient } from "../../api/client";
import type { BindZoneExistingImportUploadRequest, BindZoneImportUploadRequest, Zone } from "../../api/generated/dns-api-client";

interface ZonesState {
  items: Zone[];
  loading: boolean;
  saving: boolean;
  error: string | null;
}

const initialState: ZonesState = {
  items: [],
  loading: false,
  saving: false,
  error: null
};

export const fetchZones = createAsyncThunk("zones/fetch", async () => {
  return await apiClient.getZones();
});

export const saveZone = createAsyncThunk("zones/save", async (zone: Zone, { dispatch }) => {
  if (zone.id == null) {
    await apiClient.createZone(zone);
  } else {
    await apiClient.updateZone(zone);
  }

  await dispatch(fetchZones()).unwrap();
});

export const deleteZone = createAsyncThunk("zones/delete", async (zoneId: number, { dispatch }) => {
  await apiClient.deleteZone(zoneId);
  await dispatch(fetchZones()).unwrap();
});

export const importBindZone = createAsyncThunk(
  "zones/importBindZone",
  async (request: BindZoneImportUploadRequest, { dispatch }) => {
    await apiClient.importBindZoneFile(request);
    await dispatch(fetchZones()).unwrap();
  }
);

export const importBindZoneIntoZone = createAsyncThunk(
  "zones/importBindZoneIntoZone",
  async (request: BindZoneExistingImportUploadRequest, { dispatch }) => {
    await apiClient.importBindZoneIntoExistingZone(request);
    await dispatch(fetchZones()).unwrap();
  }
);

const zonesSlice = createSlice({
  name: "zones",
  initialState,
  reducers: {},
  extraReducers: (builder) => {
    builder
      .addCase(fetchZones.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(fetchZones.fulfilled, (state, action) => {
        state.loading = false;
        state.items = action.payload;
      })
      .addCase(fetchZones.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message ?? "Failed to load zones";
      })
      .addCase(saveZone.pending, (state) => {
        state.saving = true;
        state.error = null;
      })
      .addCase(saveZone.fulfilled, (state) => {
        state.saving = false;
      })
      .addCase(saveZone.rejected, (state, action) => {
        state.saving = false;
        state.error = action.error.message ?? "Failed to save zone";
      })
      .addCase(deleteZone.pending, (state) => {
        state.saving = true;
        state.error = null;
      })
      .addCase(deleteZone.fulfilled, (state) => {
        state.saving = false;
      })
      .addCase(deleteZone.rejected, (state, action) => {
        state.saving = false;
        state.error = action.error.message ?? "Failed to delete zone";
      })
      .addCase(importBindZone.pending, (state) => {
        state.saving = true;
        state.error = null;
      })
      .addCase(importBindZone.fulfilled, (state) => {
        state.saving = false;
      })
      .addCase(importBindZone.rejected, (state, action) => {
        state.saving = false;
        state.error = action.error.message ?? "Failed to import BIND zone";
      })
      .addCase(importBindZoneIntoZone.pending, (state) => {
        state.saving = true;
        state.error = null;
      })
      .addCase(importBindZoneIntoZone.fulfilled, (state) => {
        state.saving = false;
      })
      .addCase(importBindZoneIntoZone.rejected, (state, action) => {
        state.saving = false;
        state.error = action.error.message ?? "Failed to import BIND zone into zone";
      });
  }
});

export default zonesSlice.reducer;
