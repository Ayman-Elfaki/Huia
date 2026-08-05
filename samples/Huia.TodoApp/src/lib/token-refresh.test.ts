import {afterEach, describe, expect, it, vi} from "vitest";
import {refreshAccessToken, type RefreshableToken, shouldRefresh} from "@/lib/token-refresh";

describe("shouldRefresh", () => {
    it("returns false well before expiry", () => {
        expect(shouldRefresh(100_000, 0)).toBe(false);
    });

    it("returns true inside the refresh skew window", () => {
        expect(shouldRefresh(10_000, 9_990)).toBe(true);
    });

    it("returns true once already expired", () => {
        expect(shouldRefresh(10_000, 20_000)).toBe(true);
    });

    it("returns false when there's no expiry to compare against", () => {
        expect(shouldRefresh(undefined, Date.now())).toBe(false);
    });
});

describe("refreshAccessToken", () => {
    const originalEnv = {...process.env};

    afterEach(() => {
        process.env = {...originalEnv};
        vi.unstubAllGlobals();
    });

    it("sets an error and leaves the token otherwise untouched when there's no refresh token to redeem", async () => {
        const token = {accessToken: "stale-access-token"};

        const result = await refreshAccessToken(token);

        expect(result).toEqual({accessToken: "stale-access-token", error: "RefreshAccessTokenError"});
    });

    it("rotates the access/refresh tokens and clears any prior error on success", async () => {
        process.env.AUTH_HUIA_ISSUER = "https://localhost:5041/";
        process.env.AUTH_HUIA_CLIENT_ID = "todo-web";
        process.env.AUTH_HUIA_CLIENT_SECRET = "todo-web-dev-secret";
        const fetchMock = vi.fn().mockResolvedValue({
            ok: true,
            json: async () => ({
                access_token: "fresh-access-token",
                refresh_token: "rotated-refresh-token",
                id_token: "fresh-id-token",
                expires_in: 3600,
            }),
        });
        vi.stubGlobal("fetch", fetchMock);
        const before = Date.now();

        const result = await refreshAccessToken<RefreshableToken>({
            accessToken: "stale-access-token",
            idToken: "stale-id-token",
            refreshToken: "old-refresh-token",
            error: "RefreshAccessTokenError",
        });

        expect(fetchMock).toHaveBeenCalledWith(
            "https://localhost:5041/connect/token",
            expect.objectContaining({method: "POST"}),
        );
        const body = fetchMock.mock.calls[0][1].body as URLSearchParams;
        expect(body.get("grant_type")).toBe("refresh_token");
        expect(body.get("refresh_token")).toBe("old-refresh-token");
        expect(body.get("client_id")).toBe("todo-web");

        expect(result.accessToken).toBe("fresh-access-token");
        expect(result.idToken).toBe("fresh-id-token");
        expect(result.refreshToken).toBe("rotated-refresh-token");
        expect(result.error).toBeUndefined();
        expect(result.accessTokenExpires).toBeGreaterThanOrEqual(before + 3600 * 1000);
    });

    it("keeps the redeemed refresh token when the response doesn't rotate it", async () => {
        process.env.AUTH_HUIA_ISSUER = "https://localhost:5041/";
        process.env.AUTH_HUIA_CLIENT_ID = "todo-web";
        process.env.AUTH_HUIA_CLIENT_SECRET = "todo-web-dev-secret";
        vi.stubGlobal(
            "fetch",
            vi.fn().mockResolvedValue({
                ok: true,
                json: async () => ({access_token: "fresh-access-token", expires_in: 3600}),
            }),
        );

        const result = await refreshAccessToken({refreshToken: "old-refresh-token"});

        expect(result.refreshToken).toBe("old-refresh-token");
    });

    it("sets error when the token endpoint rejects the refresh token", async () => {
        process.env.AUTH_HUIA_ISSUER = "https://localhost:5041/";
        process.env.AUTH_HUIA_CLIENT_ID = "todo-web";
        process.env.AUTH_HUIA_CLIENT_SECRET = "todo-web-dev-secret";
        vi.stubGlobal(
            "fetch",
            vi.fn().mockResolvedValue({
                ok: false,
                json: async () => ({error: "invalid_grant", error_description: "The refresh token is no longer valid."}),
            }),
        );

        const result = await refreshAccessToken<RefreshableToken>({
            accessToken: "stale-access-token",
            refreshToken: "revoked-refresh-token",
        });

        expect(result.error).toBe("RefreshAccessTokenError");
        // The stale access token is left in place rather than cleared — a caller checking `error` first
        // won't use it, but nothing here should silently discard data on a failed refresh.
        expect(result.accessToken).toBe("stale-access-token");
    });
});
