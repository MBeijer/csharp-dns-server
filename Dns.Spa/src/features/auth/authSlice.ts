import { createAsyncThunk, createSlice } from "@reduxjs/toolkit";
import { apiClient } from "../../api/client";

const AUTH_TOKEN_STORAGE_KEY = "dns_spa_auth_token";

interface AuthState {
  token: string | null;
  loading: boolean;
  error: string | null;
}

function normalizeToken(rawToken: string): string {
  const trimmed = rawToken.trim();
  if (trimmed.startsWith("\"") && trimmed.endsWith("\"")) {
    try {
      const parsed = JSON.parse(trimmed);
      if (typeof parsed === "string") {
        return parsed.trim();
      }
    } catch {
      // Fall back to manual quote stripping below.
    }
    return trimmed.slice(1, -1).trim();
  }

  return trimmed;
}

function loadPersistedToken(): string | null {
  if (typeof window === "undefined") {
    return null;
  }

  const stored = window.localStorage.getItem(AUTH_TOKEN_STORAGE_KEY);
  if (stored == null || stored.trim().length === 0) {
    return null;
  }

  return normalizeToken(stored);
}

function persistToken(token: string | null): void {
  if (typeof window === "undefined") {
    return;
  }

  if (token == null) {
    window.localStorage.removeItem(AUTH_TOKEN_STORAGE_KEY);
    return;
  }

  window.localStorage.setItem(AUTH_TOKEN_STORAGE_KEY, token);
}

const persistedToken = loadPersistedToken();
apiClient.setBearerToken(persistedToken);

const initialState: AuthState = {
  token: persistedToken,
  loading: false,
  error: null
};

export const login = createAsyncThunk(
  "auth/login",
  async (payload: { account: string; password: string }) => {
    const token = await apiClient.login(payload.account, payload.password);
    return normalizeToken(token);
  }
);

const authSlice = createSlice({
  name: "auth",
  initialState,
  reducers: {
    logout(state) {
      state.token = null;
      state.error = null;
      apiClient.setBearerToken(null);
      persistToken(null);
    }
  },
  extraReducers: (builder) => {
    builder
      .addCase(login.pending, (state) => {
        state.loading = true;
        state.error = null;
      })
      .addCase(login.fulfilled, (state, action) => {
        state.loading = false;
        state.token = action.payload;
        apiClient.setBearerToken(action.payload);
        persistToken(action.payload);
      })
      .addCase(login.rejected, (state, action) => {
        state.loading = false;
        state.error = action.error.message ?? "Login failed";
      });
  }
});

export const { logout } = authSlice.actions;
export default authSlice.reducer;
