import { useEffect, useMemo, useState } from "react";
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Checkbox,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControl,
  FormControlLabel,
  IconButton,
  InputLabel,
  MenuItem,
  Select,
  Stack,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  TableSortLabel,
  TextField,
  Tooltip,
  Typography
} from "@mui/material";
import AddIcon from "@mui/icons-material/Add";
import DeleteIcon from "@mui/icons-material/Delete";
import DnsIcon from "@mui/icons-material/Dns";
import EditIcon from "@mui/icons-material/Edit";
import RefreshIcon from "@mui/icons-material/Refresh";
import { useAppDispatch, useAppSelector } from "../app/hooks";
import type { Zone, ZoneRecord } from "../api/generated/dns-api-client";
import { deleteZone, fetchZones, saveZone } from "../features/zones/zonesSlice";

const RESOURCE_TYPES = ["A", "AAAA", "CNAME", "NS", "MX", "TXT", "PTR", "SRV"];
const RESOURCE_CLASSES = ["IN", "CS", "CH", "HS"];

type EnabledBulkMode = "keep" | "enable" | "disable";
type SortDirection = "asc" | "desc";
type ZoneSortKey = "suffix" | "serial" | "enabled" | "records" | "relationship";
type RecordSortKey = "host" | "type" | "class" | "data";

interface EditableZone {
  id?: number;
  suffix: string;
  serial: number;
  enabled: boolean;
  masterZoneId?: number | null;
  soaRecordId?: number;
  soaPrimaryNs: string;
  soaHostmaster: string;
  soaRefresh: string;
  soaRetry: string;
  soaExpiry: string;
  soaMinimum: string;
  records: ZoneRecord[];
}

interface SoaFields {
  soaRecordId?: number;
  soaPrimaryNs: string;
  soaHostmaster: string;
  soaRefresh: string;
  soaRetry: string;
  soaExpiry: string;
  soaMinimum: string;
}

const DEFAULT_SOA: SoaFields = {
  soaPrimaryNs: "ns1.eevul.net.",
  soaHostmaster: "hostmaster.eevul.net.",
  soaRefresh: "1H",
  soaRetry: "15M",
  soaExpiry: "1W",
  soaMinimum: "1D"
};

function emptyZone(): EditableZone {
  return {
    suffix: "",
    serial: 1,
    enabled: true,
    masterZoneId: null,
    ...DEFAULT_SOA,
    records: []
  };
}

function parseSoaData(rawData: string | undefined): Partial<SoaFields> {
  if (!rawData) {
    return {};
  }

  const normalized = rawData
    .replace(/[()]/g, " ")
    .split("\n")
    .map((line) => line.split(";")[0]?.trim() ?? "")
    .join(" ");

  const parts = normalized.split(/\s+/).filter((token) => token.length > 0);
  if (parts.length < 7) {
    return {};
  }

  return {
    soaPrimaryNs: parts[0],
    soaHostmaster: parts[1],
    soaRefresh: parts[3],
    soaRetry: parts[4],
    soaExpiry: parts[5],
    soaMinimum: parts[6]
  };
}

function buildSoaRecord(zone: EditableZone): ZoneRecord {
  return {
    id: zone.soaRecordId,
    host: "@",
    type: "SOA",
    class: "IN",
    data: `${zone.soaPrimaryNs.trim()} ${zone.soaHostmaster.trim()} ${zone.serial} ${zone.soaRefresh.trim()} ${zone.soaRetry.trim()} ${zone.soaExpiry.trim()} ${zone.soaMinimum.trim()}`
  };
}

function mapZoneToEditable(zone: Zone): EditableZone {
  const soaRecord = (zone.records ?? []).find((record) => (record.type ?? "").toUpperCase() === "SOA");
  const soaParsed = parseSoaData(soaRecord?.data);
  const nonSoaRecords = (zone.records ?? []).filter((record) => (record.type ?? "").toUpperCase() !== "SOA");

  return {
    id: zone.id,
    suffix: zone.suffix ?? "",
    serial: zone.serial ?? 1,
    enabled: zone.enabled ?? true,
    masterZoneId: zone.masterZoneId ?? null,
    soaRecordId: soaRecord?.id,
    soaPrimaryNs: soaParsed.soaPrimaryNs ?? DEFAULT_SOA.soaPrimaryNs,
    soaHostmaster: soaParsed.soaHostmaster ?? DEFAULT_SOA.soaHostmaster,
    soaRefresh: soaParsed.soaRefresh ?? DEFAULT_SOA.soaRefresh,
    soaRetry: soaParsed.soaRetry ?? DEFAULT_SOA.soaRetry,
    soaExpiry: soaParsed.soaExpiry ?? DEFAULT_SOA.soaExpiry,
    soaMinimum: soaParsed.soaMinimum ?? DEFAULT_SOA.soaMinimum,
    records: nonSoaRecords.map((record) => ({ ...record }))
  };
}

function normalizeZoneForApi(zone: EditableZone): Zone {
  const soaRecord = buildSoaRecord(zone);

  return {
    id: zone.id,
    suffix: zone.suffix.trim(),
    serial: zone.serial,
    enabled: zone.enabled,
    masterZoneId: zone.masterZoneId ?? null,
    records: [
      soaRecord,
      ...zone.records
      .map((record) => ({
        id: record.id,
        host: record.host?.trim() ?? "",
        type: record.type?.trim() ?? "",
        class: record.class?.trim() ?? "",
        data: record.data?.trim() ?? ""
      }))
      .filter((record) => record.host && record.type && record.class && record.data)
    ]
  };
}

export function ZoneList(): JSX.Element {
  const dispatch = useAppDispatch();
  const zones = useAppSelector((state) => state.zones);

  const [selectedZoneIds, setSelectedZoneIds] = useState<number[]>([]);

  const [editing, setEditing] = useState(false);
  const [zoneDraft, setZoneDraft] = useState<EditableZone>(emptyZone());

  const [recordDialogOpen, setRecordDialogOpen] = useState(false);
  const [recordDraft, setRecordDraft] = useState<ZoneRecord>({ host: "", type: "A", class: "IN", data: "" });
  const [recordEditIndex, setRecordEditIndex] = useState<number | null>(null);

  const [bulkDialogOpen, setBulkDialogOpen] = useState(false);
  const [bulkEnabledMode, setBulkEnabledMode] = useState<EnabledBulkMode>("keep");
  const [zoneSortKey, setZoneSortKey] = useState<ZoneSortKey>("suffix");
  const [zoneSortDirection, setZoneSortDirection] = useState<SortDirection>("asc");
  const [recordSortKey, setRecordSortKey] = useState<RecordSortKey>("host");
  const [recordSortDirection, setRecordSortDirection] = useState<SortDirection>("asc");

  useEffect(() => {
    void dispatch(fetchZones());
  }, [dispatch]);

  const sortedZones = useMemo(() => {
    const directionFactor = zoneSortDirection === "asc" ? 1 : -1;

    return [...zones.items].sort((a, b) => {
      let compare = 0;
      if (zoneSortKey === "suffix") {
        compare = (a.suffix ?? "").localeCompare(b.suffix ?? "");
      } else if (zoneSortKey === "serial") {
        compare = (a.serial ?? 0) - (b.serial ?? 0);
      } else if (zoneSortKey === "enabled") {
        compare = Number(a.enabled ?? false) - Number(b.enabled ?? false);
      } else if (zoneSortKey === "records") {
        compare = (a.records?.length ?? 0) - (b.records?.length ?? 0);
      } else if (zoneSortKey === "relationship") {
        const relationshipA = a.masterZoneId != null ? `slave-${a.masterZoneSuffix ?? ""}` : `master-${a.slaveZoneCount ?? 0}`;
        const relationshipB = b.masterZoneId != null ? `slave-${b.masterZoneSuffix ?? ""}` : `master-${b.slaveZoneCount ?? 0}`;
        compare = relationshipA.localeCompare(relationshipB);
      }

      return compare * directionFactor;
    });
  }, [zones.items, zoneSortDirection, zoneSortKey]);

  const sortedDraftRecords = useMemo(() => {
    const directionFactor = recordSortDirection === "asc" ? 1 : -1;
    const withIndex = zoneDraft.records.map((record, originalIndex) => ({ record, originalIndex }));

    return withIndex.sort((a, b) => {
      let compare = 0;
      if (recordSortKey === "host") {
        compare = (a.record.host ?? "").localeCompare(b.record.host ?? "");
      } else if (recordSortKey === "type") {
        compare = (a.record.type ?? "").localeCompare(b.record.type ?? "");
      } else if (recordSortKey === "class") {
        compare = (a.record.class ?? "").localeCompare(b.record.class ?? "");
      } else if (recordSortKey === "data") {
        compare = (a.record.data ?? "").localeCompare(b.record.data ?? "");
      }

      return compare * directionFactor;
    });
  }, [recordSortDirection, recordSortKey, zoneDraft.records]);

  const selectableIds = useMemo(
    () => sortedZones.map((zone) => zone.id).filter((id): id is number => id != null),
    [sortedZones]
  );

  const allSelected = selectableIds.length > 0 && selectableIds.every((id) => selectedZoneIds.includes(id));
  const selectedCount = selectedZoneIds.length;
  const isDraftSlave = zoneDraft.masterZoneId != null;

  const toggleSelectAll = () => {
    if (allSelected) {
      setSelectedZoneIds([]);
      return;
    }

    setSelectedZoneIds(selectableIds);
  };

  const toggleSelectZone = (zoneId: number) => {
    setSelectedZoneIds((current) =>
      current.includes(zoneId) ? current.filter((id) => id !== zoneId) : [...current, zoneId]
    );
  };

  const requestZoneSort = (key: ZoneSortKey) => {
    if (zoneSortKey === key) {
      setZoneSortDirection((current) => (current === "asc" ? "desc" : "asc"));
      return;
    }

    setZoneSortKey(key);
    setZoneSortDirection("asc");
  };

  const requestRecordSort = (key: RecordSortKey) => {
    if (recordSortKey === key) {
      setRecordSortDirection((current) => (current === "asc" ? "desc" : "asc"));
      return;
    }

    setRecordSortKey(key);
    setRecordSortDirection("asc");
  };

  const startCreateZone = () => {
    setZoneDraft(emptyZone());
    setEditing(true);
  };

  const startEditZone = (zone: Zone) => {
    setZoneDraft(mapZoneToEditable(zone));
    setEditing(true);
  };

  const cancelEditZone = () => {
    setZoneDraft(emptyZone());
    setEditing(false);
  };

  const submitZone = async () => {
    if (zoneDraft.suffix.trim().length === 0) {
      return;
    }

    await dispatch(saveZone(normalizeZoneForApi(zoneDraft))).unwrap();
    cancelEditZone();
  };

  const removeZone = async (zone: Zone) => {
    if (zone.id == null) {
      return;
    }

    if (!window.confirm(`Delete zone '${zone.suffix}'?`)) {
      return;
    }

    await dispatch(deleteZone(zone.id)).unwrap();
    setSelectedZoneIds((current) => current.filter((id) => id !== zone.id));
  };

  const removeSelectedZones = async () => {
    if (selectedZoneIds.length === 0) {
      return;
    }

    if (!window.confirm(`Delete ${selectedZoneIds.length} selected zone(s)?`)) {
      return;
    }

    for (const zoneId of selectedZoneIds) {
      await dispatch(deleteZone(zoneId)).unwrap();
    }

    setSelectedZoneIds([]);
  };

  const applyBulkEdit = async () => {
    if (selectedZoneIds.length === 0) {
      return;
    }

    const selectedZones = sortedZones.filter((zone): zone is Zone & { id: number } =>
      zone.id != null ? selectedZoneIds.includes(zone.id) : false
    );

    const editableZones = selectedZones.filter((zone) => zone.masterZoneId == null);
    for (const zone of editableZones) {
      const draft = mapZoneToEditable(zone);

      if (bulkEnabledMode === "enable") {
        draft.enabled = true;
      } else if (bulkEnabledMode === "disable") {
        draft.enabled = false;
      }

      await dispatch(saveZone(normalizeZoneForApi(draft))).unwrap();
    }

    setBulkDialogOpen(false);
    setBulkEnabledMode("keep");
  };

  const openCreateRecord = () => {
    if (isDraftSlave) {
      return;
    }

    setRecordEditIndex(null);
    setRecordDraft({ host: "", type: "A", class: "IN", data: "" });
    setRecordDialogOpen(true);
  };

  const openEditRecord = (index: number) => {
    if (isDraftSlave) {
      return;
    }

    setRecordEditIndex(index);
    setRecordDraft({ ...zoneDraft.records[index] });
    setRecordDialogOpen(true);
  };

  const saveRecord = () => {
    const next = { ...recordDraft };
    setZoneDraft((current) => {
      const records = [...current.records];
      if (recordEditIndex == null) {
        records.push(next);
      } else {
        records[recordEditIndex] = next;
      }

      return {
        ...current,
        records
      };
    });

    setRecordDialogOpen(false);
    setRecordEditIndex(null);
  };

  const deleteRecord = (index: number) => {
    if (isDraftSlave) {
      return;
    }

    setZoneDraft((current) => ({
      ...current,
      records: current.records.filter((_, i) => i !== index)
    }));
  };

  const availableMasterZones = useMemo(() => {
    return sortedZones.filter((zone) => {
      if (zone.id == null) return false;
      if (zone.id === zoneDraft.id) return false;
      return zone.masterZoneId == null;
    });
  }, [sortedZones, zoneDraft.id]);

  const getRelationshipLabel = (zone: Zone): string => {
    if (zone.masterZoneId != null) {
      return `Slave of ${zone.masterZoneSuffix ?? `#${zone.masterZoneId}`}`;
    }

    if ((zone.slaveZoneCount ?? 0) > 0) {
      return `Master (${zone.slaveZoneCount} slave${(zone.slaveZoneCount ?? 0) === 1 ? "" : "s"})`;
    }

    return "Standalone";
  };

  return (
    <Stack spacing={2}>
      <Card elevation={2}>
        <CardContent>
          <Stack spacing={2}>
            <Stack direction="row" justifyContent="space-between" alignItems="center" gap={2}>
              <Box>
                <Typography variant="h6" fontWeight={600} sx={{ display: "flex", alignItems: "center", gap: 1 }}>
                  <DnsIcon fontSize="small" /> Zones Overview
                </Typography>
                <Typography variant="body2" color="text.secondary">
                  Select zones with checkboxes for bulk actions, or edit one zone at a time.
                </Typography>
              </Box>

              <Stack direction="row" spacing={1} flexWrap="wrap">
                <Button
                  variant="outlined"
                  startIcon={<RefreshIcon />}
                  onClick={() => void dispatch(fetchZones())}
                  disabled={zones.loading || zones.saving}
                >
                  Refresh
                </Button>
                <Button variant="contained" startIcon={<AddIcon />} onClick={startCreateZone} disabled={zones.saving}>
                  Add zone
                </Button>
                <Button
                  variant="outlined"
                  startIcon={<EditIcon />}
                  disabled={zones.saving || selectedCount === 0}
                  onClick={() => setBulkDialogOpen(true)}
                >
                  Bulk edit ({selectedCount})
                </Button>
                <Button
                  variant="outlined"
                  color="error"
                  startIcon={<DeleteIcon />}
                  disabled={zones.saving || selectedCount === 0}
                  onClick={() => void removeSelectedZones()}
                >
                  Bulk delete ({selectedCount})
                </Button>
              </Stack>
            </Stack>

            {zones.loading ? (
              <Stack direction="row" spacing={1} alignItems="center">
                <CircularProgress size={18} />
                <Typography variant="body2">Loading zones...</Typography>
              </Stack>
            ) : null}

            {zones.error ? <Alert severity="error">{zones.error}</Alert> : null}

            <TableContainer>
              <Table size="small">
                <TableHead>
                  <TableRow>
                    <TableCell padding="checkbox">
                      <Checkbox
                        checked={allSelected}
                        indeterminate={!allSelected && selectedCount > 0}
                        onChange={toggleSelectAll}
                        inputProps={{ "aria-label": "select all zones" }}
                      />
                    </TableCell>
                    <TableCell sortDirection={zoneSortKey === "suffix" ? zoneSortDirection : false}>
                      <TableSortLabel
                        active={zoneSortKey === "suffix"}
                        direction={zoneSortKey === "suffix" ? zoneSortDirection : "asc"}
                        onClick={() => requestZoneSort("suffix")}
                      >
                        Suffix
                      </TableSortLabel>
                    </TableCell>
                    <TableCell sortDirection={zoneSortKey === "serial" ? zoneSortDirection : false}>
                      <TableSortLabel
                        active={zoneSortKey === "serial"}
                        direction={zoneSortKey === "serial" ? zoneSortDirection : "asc"}
                        onClick={() => requestZoneSort("serial")}
                      >
                        Serial
                      </TableSortLabel>
                    </TableCell>
                    <TableCell sortDirection={zoneSortKey === "enabled" ? zoneSortDirection : false}>
                      <TableSortLabel
                        active={zoneSortKey === "enabled"}
                        direction={zoneSortKey === "enabled" ? zoneSortDirection : "asc"}
                        onClick={() => requestZoneSort("enabled")}
                      >
                        Enabled
                      </TableSortLabel>
                    </TableCell>
                    <TableCell sortDirection={zoneSortKey === "records" ? zoneSortDirection : false}>
                      <TableSortLabel
                        active={zoneSortKey === "records"}
                        direction={zoneSortKey === "records" ? zoneSortDirection : "asc"}
                        onClick={() => requestZoneSort("records")}
                      >
                        Records
                      </TableSortLabel>
                    </TableCell>
                    <TableCell sortDirection={zoneSortKey === "relationship" ? zoneSortDirection : false}>
                      <TableSortLabel
                        active={zoneSortKey === "relationship"}
                        direction={zoneSortKey === "relationship" ? zoneSortDirection : "asc"}
                        onClick={() => requestZoneSort("relationship")}
                      >
                        Relationship
                      </TableSortLabel>
                    </TableCell>
                    <TableCell align="right">Actions</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {sortedZones.map((zone) => {
                    const zoneId = zone.id;
                    const isChecked = zoneId != null && selectedZoneIds.includes(zoneId);

                    return (
                      <TableRow key={zone.id ?? zone.suffix} hover>
                        <TableCell padding="checkbox">
                          <Checkbox
                            checked={isChecked}
                            disabled={zoneId == null}
                            onChange={() => {
                              if (zoneId != null) {
                                toggleSelectZone(zoneId);
                              }
                            }}
                          />
                        </TableCell>
                        <TableCell>{zone.suffix}</TableCell>
                        <TableCell>{zone.serial}</TableCell>
                        <TableCell>
                          <Chip
                            size="small"
                            color={zone.enabled ? "success" : "default"}
                            label={zone.enabled ? "enabled" : "disabled"}
                          />
                        </TableCell>
                        <TableCell>{zone.records?.length ?? 0}</TableCell>
                        <TableCell>
                          <Typography variant="body2">{getRelationshipLabel(zone)}</Typography>
                        </TableCell>
                        <TableCell align="right">
                          <Stack direction="row" spacing={1} justifyContent="flex-end">
                            <Tooltip title={zone.masterZoneId != null ? "Slave zones are synchronized from their master and cannot be edited directly." : "Edit zone"}>
                              <span>
                                <IconButton
                                  size="small"
                                  color="primary"
                                  onClick={() => startEditZone(zone)}
                                  disabled={zone.masterZoneId != null}
                                >
                                  <EditIcon fontSize="small" />
                                </IconButton>
                              </span>
                            </Tooltip>
                            <Tooltip title="Delete zone">
                              <IconButton size="small" color="error" onClick={() => void removeZone(zone)}>
                                <DeleteIcon fontSize="small" />
                              </IconButton>
                            </Tooltip>
                          </Stack>
                        </TableCell>
                      </TableRow>
                    );
                  })}
                  {sortedZones.length === 0 ? (
                    <TableRow>
                      <TableCell colSpan={7}>
                        <Typography variant="body2" color="text.secondary">
                          No zones found.
                        </Typography>
                      </TableCell>
                    </TableRow>
                  ) : null}
                </TableBody>
              </Table>
            </TableContainer>
          </Stack>
        </CardContent>
      </Card>

      <Dialog open={editing} onClose={cancelEditZone} maxWidth="md" fullWidth>
        <DialogTitle>{zoneDraft.id == null ? "Create zone" : `Edit zone: ${zoneDraft.suffix}`}</DialogTitle>
        <DialogContent
          dividers
          sx={{
            maxHeight: "70vh",
            overflowY: "auto",
            scrollbarWidth: "thin",
            scrollbarColor: "#4fc3f7 #1a2438",
            "&::-webkit-scrollbar": {
              width: "10px"
            },
            "&::-webkit-scrollbar-track": {
              backgroundColor: "#1a2438",
              borderRadius: "999px"
            },
            "&::-webkit-scrollbar-thumb": {
              background: "linear-gradient(180deg, #4fc3f7, #80cbc4)",
              borderRadius: "999px",
              border: "2px solid #1a2438"
            },
            "&::-webkit-scrollbar-thumb:hover": {
              background: "linear-gradient(180deg, #81d4fa, #a5d6d1)"
            }
          }}
        >
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField
              label="Suffix"
              value={zoneDraft.suffix}
              onChange={(event) => setZoneDraft((current) => ({ ...current, suffix: event.target.value }))}
              fullWidth
            />

            <Typography variant="subtitle1" fontWeight={600}>
              SOA
            </Typography>
            <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
              <TextField
                label="Primary NS"
                value={zoneDraft.soaPrimaryNs}
                onChange={(event) => setZoneDraft((current) => ({ ...current, soaPrimaryNs: event.target.value }))}
                fullWidth
                disabled={isDraftSlave}
              />
              <TextField
                label="Hostmaster"
                value={zoneDraft.soaHostmaster}
                onChange={(event) => setZoneDraft((current) => ({ ...current, soaHostmaster: event.target.value }))}
                fullWidth
                disabled={isDraftSlave}
              />
            </Stack>
            <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
              <TextField
                label="Refresh"
                value={zoneDraft.soaRefresh}
                onChange={(event) => setZoneDraft((current) => ({ ...current, soaRefresh: event.target.value }))}
                fullWidth
                disabled={isDraftSlave}
              />
              <TextField
                label="Retry"
                value={zoneDraft.soaRetry}
                onChange={(event) => setZoneDraft((current) => ({ ...current, soaRetry: event.target.value }))}
                fullWidth
                disabled={isDraftSlave}
              />
              <TextField
                label="Expiry"
                value={zoneDraft.soaExpiry}
                onChange={(event) => setZoneDraft((current) => ({ ...current, soaExpiry: event.target.value }))}
                fullWidth
                disabled={isDraftSlave}
              />
              <TextField
                label="Minimum"
                value={zoneDraft.soaMinimum}
                onChange={(event) => setZoneDraft((current) => ({ ...current, soaMinimum: event.target.value }))}
                fullWidth
                disabled={isDraftSlave}
              />
            </Stack>

            <FormControl fullWidth>
              <InputLabel id="master-zone-label">Master zone</InputLabel>
              <Select
                labelId="master-zone-label"
                label="Master zone"
                value={zoneDraft.masterZoneId ?? ""}
                onChange={(event) => {
                  const raw = event.target.value;
                  const nextMasterId = raw === "" ? null : Number(raw);
                  setZoneDraft((current) => ({ ...current, masterZoneId: nextMasterId }));
                }}
              >
                <MenuItem value="">None (standalone/master)</MenuItem>
                {availableMasterZones.map((zone) => (
                  <MenuItem key={zone.id} value={zone.id}>
                    {zone.suffix}
                  </MenuItem>
                ))}
              </Select>
            </FormControl>

            {isDraftSlave ? (
              <Alert severity="info">
                This zone is configured as a slave. Serial, enabled state, and records are synchronized from its master.
              </Alert>
            ) : null}

            <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
              <TextField
                label="Serial"
                value={zoneDraft.serial}
                fullWidth
                disabled
                helperText="Managed automatically by server (YYYYMMDDXX)."
              />
              <FormControlLabel
                control={
                  <Switch
                    checked={zoneDraft.enabled}
                    onChange={(event) => setZoneDraft((current) => ({ ...current, enabled: event.target.checked }))}
                    disabled={isDraftSlave}
                  />
                }
                label="Enabled"
              />
            </Stack>

            <Stack spacing={1}>
              <Stack direction="row" justifyContent="space-between" alignItems="center">
                <Typography variant="subtitle1" fontWeight={600}>
                  Records
                </Typography>
                <Button size="small" startIcon={<AddIcon />} onClick={openCreateRecord} disabled={isDraftSlave}>
                  Add record
                </Button>
              </Stack>

              <TableContainer>
                <Table size="small">
                  <TableHead>
                    <TableRow>
                      <TableCell sortDirection={recordSortKey === "host" ? recordSortDirection : false}>
                        <TableSortLabel
                          active={recordSortKey === "host"}
                          direction={recordSortKey === "host" ? recordSortDirection : "asc"}
                          onClick={() => requestRecordSort("host")}
                        >
                          Host
                        </TableSortLabel>
                      </TableCell>
                      <TableCell sortDirection={recordSortKey === "type" ? recordSortDirection : false}>
                        <TableSortLabel
                          active={recordSortKey === "type"}
                          direction={recordSortKey === "type" ? recordSortDirection : "asc"}
                          onClick={() => requestRecordSort("type")}
                        >
                          Type
                        </TableSortLabel>
                      </TableCell>
                      <TableCell sortDirection={recordSortKey === "class" ? recordSortDirection : false}>
                        <TableSortLabel
                          active={recordSortKey === "class"}
                          direction={recordSortKey === "class" ? recordSortDirection : "asc"}
                          onClick={() => requestRecordSort("class")}
                        >
                          Class
                        </TableSortLabel>
                      </TableCell>
                      <TableCell sortDirection={recordSortKey === "data" ? recordSortDirection : false}>
                        <TableSortLabel
                          active={recordSortKey === "data"}
                          direction={recordSortKey === "data" ? recordSortDirection : "asc"}
                          onClick={() => requestRecordSort("data")}
                        >
                          Data
                        </TableSortLabel>
                      </TableCell>
                      <TableCell align="right">Actions</TableCell>
                    </TableRow>
                  </TableHead>
                  <TableBody>
                    {sortedDraftRecords.map(({ record, originalIndex }) => (
                      <TableRow key={`${record.id ?? "edit"}-${originalIndex}`}>
                        <TableCell>{record.host}</TableCell>
                        <TableCell>{record.type}</TableCell>
                        <TableCell>{record.class}</TableCell>
                        <TableCell>{record.data}</TableCell>
                        <TableCell align="right">
                          <Stack direction="row" spacing={1} justifyContent="flex-end">
                            <Tooltip title="Edit record">
                              <span>
                                <IconButton
                                  size="small"
                                  color="primary"
                                  onClick={() => openEditRecord(originalIndex)}
                                  disabled={isDraftSlave}
                                >
                                  <EditIcon fontSize="small" />
                                </IconButton>
                              </span>
                            </Tooltip>
                            <Tooltip title="Delete record">
                              <span>
                                <IconButton
                                  size="small"
                                  color="error"
                                  onClick={() => deleteRecord(originalIndex)}
                                  disabled={isDraftSlave}
                                >
                                  <DeleteIcon fontSize="small" />
                                </IconButton>
                              </span>
                            </Tooltip>
                          </Stack>
                        </TableCell>
                      </TableRow>
                    ))}
                    {sortedDraftRecords.length === 0 ? (
                      <TableRow>
                        <TableCell colSpan={5}>
                          <Typography variant="body2" color="text.secondary">
                            No records configured.
                          </Typography>
                        </TableCell>
                      </TableRow>
                    ) : null}
                  </TableBody>
                </Table>
              </TableContainer>
            </Stack>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={cancelEditZone}>Cancel</Button>
          <Button onClick={() => void submitZone()} variant="contained" disabled={zones.saving}>
            Save zone
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={recordDialogOpen} onClose={() => setRecordDialogOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>{recordEditIndex == null ? "Add record" : "Edit record"}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <TextField
              label="Host"
              value={recordDraft.host ?? ""}
              onChange={(event) => setRecordDraft((current) => ({ ...current, host: event.target.value }))}
              fullWidth
            />
            <Stack direction={{ xs: "column", sm: "row" }} spacing={2}>
              <FormControl fullWidth>
                <InputLabel id="record-type-label">Type</InputLabel>
                <Select
                  labelId="record-type-label"
                  label="Type"
                  value={recordDraft.type ?? "A"}
                  onChange={(event) => setRecordDraft((current) => ({ ...current, type: event.target.value }))}
                >
                  {RESOURCE_TYPES.map((type) => (
                    <MenuItem key={type} value={type}>
                      {type}
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
              <FormControl fullWidth>
                <InputLabel id="record-class-label">Class</InputLabel>
                <Select
                  labelId="record-class-label"
                  label="Class"
                  value={recordDraft.class ?? "IN"}
                  onChange={(event) => setRecordDraft((current) => ({ ...current, class: event.target.value }))}
                >
                  {RESOURCE_CLASSES.map((resourceClass) => (
                    <MenuItem key={resourceClass} value={resourceClass}>
                      {resourceClass}
                    </MenuItem>
                  ))}
                </Select>
              </FormControl>
            </Stack>
            <TextField
              label="Data"
              value={recordDraft.data ?? ""}
              onChange={(event) => setRecordDraft((current) => ({ ...current, data: event.target.value }))}
              fullWidth
            />
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setRecordDialogOpen(false)}>Cancel</Button>
          <Button onClick={saveRecord} variant="contained">
            Save record
          </Button>
        </DialogActions>
      </Dialog>

      <Dialog open={bulkDialogOpen} onClose={() => setBulkDialogOpen(false)} maxWidth="sm" fullWidth>
        <DialogTitle>Bulk edit selected zones</DialogTitle>
        <DialogContent>
          <Stack spacing={2} sx={{ mt: 1 }}>
            <Typography variant="body2" color="text.secondary">
              Applying changes to {selectedCount} selected zone(s).
            </Typography>
            <FormControl fullWidth>
              <InputLabel id="bulk-enabled-mode-label">Enabled</InputLabel>
              <Select
                labelId="bulk-enabled-mode-label"
                label="Enabled"
                value={bulkEnabledMode}
                onChange={(event) => setBulkEnabledMode(event.target.value as EnabledBulkMode)}
              >
                <MenuItem value="keep">Keep current value</MenuItem>
                <MenuItem value="enable">Enable all selected</MenuItem>
                <MenuItem value="disable">Disable all selected</MenuItem>
              </Select>
            </FormControl>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setBulkDialogOpen(false)}>Cancel</Button>
          <Button onClick={() => void applyBulkEdit()} variant="contained" disabled={zones.saving || selectedCount === 0}>
            Apply bulk edit
          </Button>
        </DialogActions>
      </Dialog>
    </Stack>
  );
}
