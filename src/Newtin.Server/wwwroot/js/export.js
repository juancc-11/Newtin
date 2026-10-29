"use strict";

/*
 * Exportar la pizarra desde el navegador. El servidor genera el archivo (SVG, Markdown, CSV y .newtin) con el mismo
 * código que usa la aplicación de escritorio; el PNG se obtiene dibujando ese SVG en un lienzo. La descarga se guarda
 * en este equipo. Pide el código de acceso, igual que la conexión en tiempo real.
 */
const NewtinExport = (() => {
  const EXTENSIONS = { png: "png", svg: "svg", markdown: "md", csv: "csv", newtin: "newtin" };
  const MAX_PNG_SIDE = 6000;

  function stamp() {
    const now = new Date();
    const pad = value => String(value).padStart(2, "0");
    return `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}-${pad(now.getHours())}${pad(now.getMinutes())}`;
  }

  async function fetchExport(format, code) {
    let response;
    try {
      response = await fetch(`/api/export/${format}`, {
        headers: { Authorization: `Bearer ${code}` },
        cache: "no-store"
      });
    } catch {
      throw new Error("No se pudo conectar con el servidor.");
    }

    if (response.status === 409) throw new Error("La pizarra está vacía: no hay nada que exportar.");
    if (response.status === 401) throw new Error("El código de acceso ya no es válido. Vuelve a conectarte.");
    if (response.status === 429) throw new Error("Demasiados intentos fallidos. Espera unos minutos.");
    if (!response.ok) throw new Error("El servidor no pudo generar el archivo.");
    return response;
  }

  // El SVG no tiene recursos externos, así que el lienzo no queda marcado como contaminado y se puede exportar.
  async function svgToPng(svgText) {
    const root = new DOMParser().parseFromString(svgText, "image/svg+xml").documentElement;
    const width = parseFloat(root.getAttribute("width"));
    const height = parseFloat(root.getAttribute("height"));
    if (!(width > 0) || !(height > 0)) throw new Error("No se pudo calcular el tamaño de la imagen.");

    const scale = Math.min(2, MAX_PNG_SIDE / Math.max(width, height));
    const image = new Image();
    image.src = "data:image/svg+xml;charset=utf-8," + encodeURIComponent(svgText);
    await image.decode();

    const canvas = document.createElement("canvas");
    canvas.width = Math.round(width * scale);
    canvas.height = Math.round(height * scale);
    canvas.getContext("2d").drawImage(image, 0, 0, canvas.width, canvas.height);

    return new Promise((resolve, reject) =>
      canvas.toBlob(blob => (blob ? resolve(blob) : reject(new Error("No se pudo crear la imagen PNG."))), "image/png"));
  }

  function save(blob, fileName) {
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = fileName;
    link.hidden = true;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
  }

  function init({ openButton, modal, getCode, notify }) {
    const status = modal.querySelector("[data-export-status]");
    const closeButton = modal.querySelector("[data-export-close]");
    const backdrop = modal.querySelector(".modal-backdrop");
    const options = [...modal.querySelectorAll("[data-export]")];
    let previousFocus = null;

    function setBusy(busy) {
      for (const option of options) option.disabled = busy;
    }

    function open() {
      previousFocus = document.activeElement;
      status.textContent = "";
      modal.hidden = false;
      options[0].focus();
    }

    function close() {
      if (modal.hidden) return;
      modal.hidden = true;
      if (previousFocus && typeof previousFocus.focus === "function") previousFocus.focus();
    }

    async function run(format) {
      const code = getCode();
      if (!code) {
        notify("Conéctate a la pizarra para exportarla.");
        return;
      }

      setBusy(true);
      status.textContent = "Preparando el archivo…";

      try {
        const baseName = `Pizarra-${stamp()}`;
        let blob;

        if (format === "png") {
          blob = await svgToPng(await (await fetchExport("svg", code)).text());
        } else {
          blob = await (await fetchExport(format, code)).blob();
        }

        save(blob, `${baseName}.${EXTENSIONS[format]}`);
        status.textContent = "Descarga iniciada.";
      } catch (error) {
        status.textContent = error instanceof Error && error.message
          ? error.message
          : "No se pudo exportar la pizarra.";
      } finally {
        setBusy(false);
      }
    }

    openButton.addEventListener("click", open);
    closeButton.addEventListener("click", close);
    backdrop.addEventListener("click", close);
    document.addEventListener("keydown", event => {
      if (event.key === "Escape" && !modal.hidden) close();
    });
    for (const option of options) option.addEventListener("click", () => run(option.dataset.export));
  }

  return { init };
})();