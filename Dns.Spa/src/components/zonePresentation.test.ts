import {describe, expect, it} from "vitest";
import {getZoneRelationshipLabel, isReadOnlyZone, isSlaveZone} from "./zonePresentation";

describe("zonePresentation", () => {
    it("marks runtime provider zones as read-only", () => {
        const zone = {source: "Traefik", isReadOnly: true};

        expect(isReadOnlyZone(zone)).toBe(true);
        expect(isSlaveZone(zone)).toBe(false);
        expect(getZoneRelationshipLabel(zone)).toBe("Provider managed");
    });

    it("identifies replicated zones as synced secondaries", () => {
        const zone = {source: "Secondary (primary.example:53)", isReadOnly: true, isReplicated: true};

        expect(isReadOnlyZone(zone)).toBe(true);
        expect(isSlaveZone(zone)).toBe(true);
        expect(getZoneRelationshipLabel(zone)).toBe("Synced secondary");
    });

    it("preserves database master and slave relationship labels", () => {
        expect(getZoneRelationshipLabel({masterZoneId: 4, masterZoneSuffix: "master.example"})).toBe(
            "Slave of master.example"
        );
        expect(getZoneRelationshipLabel({slaveZoneCount: 2})).toBe("Master (2 slaves)");
    });
});