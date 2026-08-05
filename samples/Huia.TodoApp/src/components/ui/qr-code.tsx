"use client";

import {useEffect, useState} from "react";
import QRCode from "qrcode";

import {cn} from "@/lib/utils";

/** Renders `value` (e.g. an `otpauth://` URI) as a scannable QR code, so setting up an authenticator app
 * doesn't require manually retyping the shared key. */
export function QrCode({value, size = 176, className}: { value: string; size?: number; className?: string }) {
    const [dataUrl, setDataUrl] = useState<string | null>(null);

    useEffect(() => {
        let cancelled = false;
        QRCode.toDataURL(value, {width: size, margin: 1}).then((url) => {
            if (!cancelled) setDataUrl(url);
        });
        return () => {
            cancelled = true;
        };
    }, [value, size]);

    if (!dataUrl) {
        return (
            <div
                className={cn("animate-pulse rounded-md bg-muted", className)}
                style={{width: size, height: size}}
            />
        );
    }

    // eslint-disable-next-line @next/next/no-img-element -- a data: URI can't go through next/image's optimizer
    return <img src={dataUrl} alt="" width={size} height={size} className={cn("rounded-md", className)} />;
}
