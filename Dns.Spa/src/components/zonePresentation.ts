import type {Zone} from "../api/generated/dns-api-client";

export function isReadOnlyZone(zone: Zone): boolean {
    return zone.isReadOnly === true || zone.masterZoneId != null;
}

export function isSlaveZone(zone: Zone): boolean {
    return zone.isReplicated === true || zone.masterZoneId != null;
}

export function getZoneRelationshipLabel(zone: Zone): string {
    if (zone.isReplicated) {
        return "Synced secondary";
    }

    if (zone.masterZoneId != null) {
        return `Slave of ${zone.masterZoneSuffix ?? `#${zone.masterZoneId}`}`;
    }

    if (zone.isReadOnly) {
        return "Provider managed";
    }

    if ((zone.slaveZoneCount ?? 0) > 0) {
        return `Master (${zone.slaveZoneCount} slave${(zone.slaveZoneCount ?? 0) === 1 ? "" : "s"})`;
    }

    return "Standalone";
}