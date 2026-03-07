import { DnsApiClient } from "./generated/dns-api-client";

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? "";

export const apiClient = new DnsApiClient(API_BASE_URL);
