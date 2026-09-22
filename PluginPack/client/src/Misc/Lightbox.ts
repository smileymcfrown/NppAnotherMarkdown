// Click an image to view it at full size in an overlay; click again or press
// Esc to close. Images inside links and the QR codes are left alone.

const OVERLAY_ID = "am-lightbox";
let installed = false;

export function InitLightbox(container: HTMLElement) {
  if (installed) {
    return;
  }
  installed = true;
  container.addEventListener("click", (e) => {
    const img = (e.target as Element | null)?.closest?.("img") as HTMLImageElement | null;
    if (!img || img.closest("a, .qrcode, .panorama") || img.classList.contains("qrcode")) {
      return;
    }
    // Only worth it when the image is shown smaller than it is.
    if (img.naturalWidth <= img.clientWidth && img.naturalHeight <= img.clientHeight) {
      return;
    }
    e.preventDefault();
    open(img.currentSrc || img.src, img.alt);
  });
  document.addEventListener("keydown", (e) => {
    if (e.key === "Escape" && document.getElementById(OVERLAY_ID)) {
      e.preventDefault();
      close();
    }
  }, true);
}

function open(src: string, alt: string) {
  close();
  const overlay = document.createElement("div");
  overlay.id = OVERLAY_ID;
  overlay.className = "am-lightbox";
  const image = document.createElement("img");
  image.src = src;
  image.alt = alt;
  overlay.appendChild(image);
  overlay.addEventListener("click", close);
  document.body.appendChild(overlay);
}

function close() {
  document.getElementById(OVERLAY_ID)?.remove();
}
