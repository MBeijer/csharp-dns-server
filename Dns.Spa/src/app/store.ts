import { configureStore } from "@reduxjs/toolkit";
import authReducer from "../features/auth/authSlice";
import zonesReducer from "../features/zones/zonesSlice";

export const store = configureStore({
  reducer: {
    auth: authReducer,
    zones: zonesReducer
  }
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
