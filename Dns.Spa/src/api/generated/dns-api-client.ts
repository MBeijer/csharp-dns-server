/*
 * This file is a checked-in placeholder so the SPA can compile before NSwag generation runs.
 * Run `npm run generate:api` to regenerate from the live OpenAPI document.
 */

export interface ZoneRecord {
  id?: number;
  host?: string;
  type?: string;
  class?: string;
  data?: string;
  zone?: number;
}

export interface Zone {
  id?: number;
  suffix?: string;
  serial?: number;
  enabled?: boolean;
  masterZoneId?: number | null;
  masterZoneSuffix?: string | null;
  slaveZoneCount?: number;
  records?: ZoneRecord[];
}

export interface BindZoneImportUploadRequest {
  file: File;
  zoneSuffix: string;
  enabled?: boolean;
  replaceExistingRecords?: boolean;
}

export interface BindZoneExistingImportUploadRequest {
  zoneId: number;
  file: File;
  replaceExistingRecords?: boolean;
}

export class DnsApiClient {
  private readonly baseUrl: string;
  private token: string | null = null;

  public constructor(baseUrl = "") {
    this.baseUrl = baseUrl;
  }

  public setBearerToken(token: string | null): void {
    this.token = this.normalizeToken(token);
  }

  public async login(account: string, password: string): Promise<string> {
    const response = await fetch(`${this.baseUrl}/user/login?account=${encodeURIComponent(account)}&password=${encodeURIComponent(password)}`);
    if (!response.ok) {
      throw new Error("Login failed");
    }

    return this.normalizeToken(await response.text()) ?? "";
  }

  public async getZones(): Promise<Zone[]> {
    const response = await fetch(`${this.baseUrl}/dns/zones`, {
      method: "GET",
      headers: this.authHeaders()
    });

    if (!response.ok) {
      throw new Error(`Failed to fetch zones: ${response.status}`);
    }

    return (await response.json()) as Zone[];
  }

  public async createZone(zone: Zone): Promise<void> {
    const response = await fetch(`${this.baseUrl}/dns/zones`, {
      method: "PUT",
      headers: this.authHeaders(true),
      body: JSON.stringify(zone)
    });

    if (!response.ok) {
      throw new Error(`Failed to create zone: ${response.status}`);
    }
  }

  public async updateZone(zone: Zone): Promise<void> {
    const response = await fetch(`${this.baseUrl}/dns/zones`, {
      method: "PATCH",
      headers: this.authHeaders(true),
      body: JSON.stringify(zone)
    });

    if (!response.ok) {
      throw new Error(`Failed to update zone: ${response.status}`);
    }
  }

  public async importBindZoneFile(request: BindZoneImportUploadRequest): Promise<void> {
    const formData = new FormData();
    formData.append("file", request.file);
    formData.append("zoneSuffix", request.zoneSuffix);
    formData.append("enabled", String(request.enabled ?? true));
    formData.append("replaceExistingRecords", String(request.replaceExistingRecords ?? true));

    const response = await fetch(`${this.baseUrl}/dns/zones/import-bind-upload`, {
      method: "POST",
      headers: this.authHeaders(),
      body: formData
    });

    if (!response.ok) {
      throw new Error(`Failed to import BIND zone: ${response.status}`);
    }
  }

  public async importBindZoneIntoExistingZone(request: BindZoneExistingImportUploadRequest): Promise<void> {
    const formData = new FormData();
    formData.append("file", request.file);
    formData.append("replaceExistingRecords", String(request.replaceExistingRecords ?? true));

    const response = await fetch(`${this.baseUrl}/dns/zones/${request.zoneId}/import-bind-upload`, {
      method: "POST",
      headers: this.authHeaders(),
      body: formData
    });

    if (!response.ok) {
      throw new Error(`Failed to import BIND zone into existing zone: ${response.status}`);
    }
  }

  public async deleteZone(id: number): Promise<void> {
    const response = await fetch(`${this.baseUrl}/dns/zones/${id}`, {
      method: "DELETE",
      headers: this.authHeaders()
    });

    if (!response.ok) {
      throw new Error(`Failed to delete zone: ${response.status}`);
    }
  }

  private authHeaders(withJsonContentType = false): Headers {
    const headers = new Headers();
    if (withJsonContentType) {
      headers.set("Content-Type", "application/json");
    }

    if (this.token) {
      headers.set("Authorization", `Bearer ${this.token}`);
    }

    return headers;
  }

  private normalizeToken(token: string | null): string | null {
    if (token == null) {
      return null;
    }

    const trimmed = token.trim();
    if (trimmed.startsWith("\"") && trimmed.endsWith("\"")) {
      try {
        const parsed = JSON.parse(trimmed);
        return typeof parsed === "string" ? parsed.trim() : trimmed.slice(1, -1).trim();
      } catch {
        return trimmed.slice(1, -1).trim();
      }
    }

    return trimmed;
  }
}
