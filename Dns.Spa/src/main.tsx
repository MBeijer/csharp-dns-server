import React from "react";
import ReactDOM from "react-dom/client";
import { Provider } from "react-redux";
import { CssBaseline, GlobalStyles, ThemeProvider, createTheme } from "@mui/material";
import App from "./App";
import { store } from "./app/store";

const theme = createTheme({
  palette: {
    mode: "dark",
    primary: {
      main: "#4fc3f7"
    },
    secondary: {
      main: "#80cbc4"
    },
    background: {
      default: "#0b1220",
      paper: "#111a2b"
    }
  },
  shape: {
    borderRadius: 10
  },
  typography: {
    fontFamily: '"IBM Plex Sans", "Segoe UI", sans-serif'
  }
});

ReactDOM.createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <Provider store={store}>
      <ThemeProvider theme={theme}>
        <CssBaseline />
        <GlobalStyles
          styles={{
            body: {
              background: "radial-gradient(circle at 0% 0%, #1f2b45 0%, #0b1220 45%, #090f1b 100%)"
            }
          }}
        />
        <App />
      </ThemeProvider>
    </Provider>
  </React.StrictMode>
);
