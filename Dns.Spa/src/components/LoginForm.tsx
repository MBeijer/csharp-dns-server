import { FormEvent, useState } from "react";
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  CircularProgress,
  Stack,
  TextField,
  Typography
} from "@mui/material";
import LoginIcon from "@mui/icons-material/Login";
import { useAppDispatch, useAppSelector } from "../app/hooks";
import { login } from "../features/auth/authSlice";

export function LoginForm(): JSX.Element {
  const dispatch = useAppDispatch();
  const auth = useAppSelector((state) => state.auth);
  const [account, setAccount] = useState("admin");
  const [password, setPassword] = useState("admin");

  const onSubmit = (event: FormEvent) => {
    event.preventDefault();
    void dispatch(login({ account, password }));
  };

  return (
    <Card elevation={2}>
      <CardContent>
        <Stack component="form" spacing={2} onSubmit={onSubmit}>
          <Box>
            <Typography variant="h6" fontWeight={600}>
              Sign in
            </Typography>
            <Typography variant="body2" color="text.secondary">
              Authenticate against /user/login to access protected DNS endpoints.
            </Typography>
          </Box>

          <TextField
            label="Account"
            value={account}
            onChange={(event) => setAccount(event.target.value)}
            autoComplete="username"
            fullWidth
          />

          <TextField
            label="Password"
            value={password}
            type="password"
            onChange={(event) => setPassword(event.target.value)}
            autoComplete="current-password"
            fullWidth
          />

          <Button
            type="submit"
            variant="contained"
            startIcon={auth.loading ? <CircularProgress size={16} color="inherit" /> : <LoginIcon />}
            disabled={auth.loading}
          >
            {auth.loading ? "Signing in..." : "Sign in"}
          </Button>

          {auth.error ? <Alert severity="error">{auth.error}</Alert> : null}
        </Stack>
      </CardContent>
    </Card>
  );
}
