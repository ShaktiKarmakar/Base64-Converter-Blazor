// Streams bytes straight off the SignalR connection as binary and hands them to the browser
// via a blob URL. The previous approach base64-encoded the payload server-side (+33% size),
// interpolated it into a data: URL, then let JS interop JSON-escape the whole thing -- several
// full copies of an already-inflated string per download.
window.downloadFileStream = async function (streamRef, filename, mimeType) {
    const buffer = await streamRef.arrayBuffer();
    const blob = new Blob([buffer], { type: mimeType || 'application/octet-stream' });
    const url = URL.createObjectURL(blob);

    try {
        const link = document.createElement('a');
        link.href = url;
        link.download = filename;
        link.style.display = 'none';
        document.body.appendChild(link);
        link.click();
        document.body.removeChild(link);
    } finally {
        // Give the browser a tick to start the download before releasing the blob.
        setTimeout(() => URL.revokeObjectURL(url), 10000);
    }
};

window.copyText = async function (text) {
    if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(text);
        return true;
    }
    return false;
};
