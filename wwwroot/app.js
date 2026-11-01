// キー操作・ドラッグ＆ドロップ・ログのスクロールだけをブラウザ側で受け持つ
window.paifu = {
    init(ref) {
        document.addEventListener("keydown", e => {
            const t = e.target.tagName;
            if (e.key === "ArrowLeft" || e.key === "ArrowRight") {
                if (t === "INPUT" || t === "SELECT") return;
                e.preventDefault();
                ref.invokeMethodAsync("OnKey", e.key);
            } else if (e.key === " " && !["BUTTON", "SELECT", "INPUT", "TEXTAREA"].includes(t)) {
                e.preventDefault();
                ref.invokeMethodAsync("OnKey", "Space");
            }
        });
        addEventListener("dragover", e => e.preventDefault());
        addEventListener("drop", async e => {
            e.preventDefault();
            const f = e.dataTransfer.files[0];
            if (f) ref.invokeMethodAsync("LoadJson", await f.text());
        });
    },
    scrollLog() {
        const c = document.querySelector("#lg .cur");
        if (c) c.scrollIntoView({ block: "nearest" });
    }
};
