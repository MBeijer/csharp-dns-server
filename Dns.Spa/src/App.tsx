import { Button, Container, Stack, Typography } from "@mui/material";
import { useAppDispatch, useAppSelector } from "./app/hooks";
import { LoginForm } from "./components/LoginForm";
import { ZoneList } from "./components/ZoneList";
import { logout } from "./features/auth/authSlice";

export default function App(): JSX.Element {
  const dispatch = useAppDispatch();
  const token = useAppSelector((state) => state.auth.token);

  return (
    <Container maxWidth="lg" sx={{ py: 4 }}>
      <Stack spacing={3}>
        <Stack direction="row" alignItems="flex-start" justifyContent="space-between" gap={2}>
          <Stack spacing={0.5}>
            <Typography variant="h4" fontWeight={700}>
              DNS Admin
            </Typography>
            <Typography color="text.secondary">
              React SPA backed by NSwag-generated client and Redux async state.
            </Typography>
          </Stack>
          {token ? (
            <Button variant="outlined" color="inherit" onClick={() => dispatch(logout())}>
              Logout
            </Button>
          ) : null}
        </Stack>
        {token ? <ZoneList /> : <LoginForm />}
      </Stack>
    </Container>
  );
}
