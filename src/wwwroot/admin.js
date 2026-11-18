// Admin panel helpers. Loaded only on /admin pages (see App.razor); Blazor calls window.adminUi.

window.adminUi = {
    showModal: dialog => dialog.showModal(),
    closeModal: dialog => dialog.close(),

    // Toasts live in #toasts, which has data-permanent: a toast shown right before a navigation stays on the next page.
    toast(message, kind = "success") {
        const toast = document.createElement("div");
        toast.className = `toast toast-${kind}`;
        toast.setAttribute("role", kind === "error" ? "alert" : "status");
        const text = document.createElement("span");
        text.textContent = message;
        const close = document.createElement("button");
        close.type = "button";
        close.className = "toast-close";
        close.setAttribute("aria-label", "Cerrar aviso");
        close.textContent = "×";
        close.onclick = () => toast.remove();
        toast.append(text, close);
        document.getElementById("toasts")?.append(toast);
        setTimeout(() => toast.remove(), kind === "error" ? 8000 : 4000);
    },
};

// Leave guard. Link clicks use Blazor's enhanced navigation, which doesn't ask NavigationLock (there's no interactive
// router), so a page with unsaved changes registers itself here and gets asked first. Pass null to release it.
let leaveGuard = null;
window.adminUi.guardLeave = dotNetPage => { leaveGuard = dotNetPage; };

document.addEventListener("click", async e => {
    const link = e.target instanceof Element ? e.target.closest("a[href]") : null;
    if (!leaveGuard || !link || link.target === "_blank" || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
    e.preventDefault();
    e.stopImmediatePropagation();
    const guard = leaveGuard;
    let leave = true;
    try {
        leave = await guard.invokeMethodAsync("ConfirmLeaveAsync");
    } catch {
        // the page is already gone: nothing left to protect
    }
    if (!leave) return;
    if (leaveGuard === guard) leaveGuard = null;
    Blazor.navigateTo(link.href);
}, true); // capture: runs before Blazor's own click handling

// Closing or reloading the tab: browsers only allow their own prompt here.
window.addEventListener("beforeunload", e => {
    if (leaveGuard) e.preventDefault();
});

// A click on the backdrop lands on the <dialog> itself (its content is inside .modal-box), so it closes the modal.
document.addEventListener("click", e => {
    if (e.target instanceof HTMLDialogElement && e.target.classList.contains("modal")) e.target.close();
});

// Drag and drop: files dropped on <label class="dropzone" for="..."> go into that input, and Blazor's InputFile
// sees a normal change. Drops anywhere else are ignored instead of making the browser open the file and lose the form.
const dropzoneAt = target => target instanceof Element ? target.closest("label.dropzone[for]") : null;

document.addEventListener("dragover", e => {
    if (!e.dataTransfer?.types.includes("Files")) return;
    e.preventDefault();
    dropzoneAt(e.target)?.classList.add("is-dragover");
});

document.addEventListener("dragleave", e => {
    const zone = dropzoneAt(e.target);
    if (zone && !zone.contains(e.relatedTarget)) zone.classList.remove("is-dragover");
});

document.addEventListener("drop", e => {
    if (!e.dataTransfer?.files.length) return;
    e.preventDefault();
    const zone = dropzoneAt(e.target);
    zone?.classList.remove("is-dragover");
    const input = zone && document.getElementById(zone.htmlFor);
    if (!input || input.disabled) return;
    const files = new DataTransfer();
    for (const file of input.multiple ? e.dataTransfer.files : [e.dataTransfer.files[0]]) files.items.add(file);
    input.files = files.files;
    input.dispatchEvent(new Event("change", { bubbles: true }));
});
