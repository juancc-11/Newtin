"use strict";

(() => {
  const $ = id => document.getElementById(id);

  const PEN_COLORS = ["#17131F", "#E60033", "#A00082", "#5420A8", "#1E6FD9", "#2EBD85", "#F2B544"];
  const NOTE_COLORS = ["#FFF1A8", "#FFC9D4", "#D6C7FF", "#C5F0DC", "#CFE6FF"];
  const COLOR_TOOLS = ["pen", "line", "arrow", "rect", "ellipse", "text"];
  const WIDTH_TOOLS = ["pen", "line", "arrow", "rect", "ellipse"];

  const login = $("login");
  const loginForm = $("join-form");
  const nameInput = $("name");
  const codeInput = $("code");
  const loginButton = $("join-button");
  const loginError = $("login-error");

  const appRoot = $("app");
  const statusPill = $("status");
  const statusText = $("status-text");
  const banner = $("banner");
  const bannerText = $("banner-text");
  const reconnectButton = $("reconnect-button");
  const toast = $("toast");

  const clientsCount = $("clients-count");
  const clientsList = $("clients-list");
  const categoriesList = $("categories-list");
  const categoryForm = $("category-form");
  const categoryName = $("category-name");
  const categoryColor = $("category-color");

  const colorOptions = $("color-options");
  const widthOptions = $("width-options");
  const textOptions = $("text-options");
  const noteOptions = $("note-options");
  const colorPalette = $("color-palette");
  const notePalette = $("note-palette");
  const customColor = $("custom-color");
  const widthInput = $("width");
  const widthValue = $("width-value");
  const fontSizeInput = $("font-size");
  const fontSizeValue = $("font-size-value");
  const deleteSelectionButton = $("delete-selection");
  const undoButton = $("undo-button");
  const redoButton = $("redo-button");
  const zoomReset = $("zoom-reset");
  const toolButtons = [...document.querySelectorAll("[data-tool]")];

  let session = null;       // { name, code } de la conexión actual
  let toastTimer = null;

  if (typeof signalR === "undefined") {
    loginError.textContent = "Falta el archivo js/signalr.min.js en el servidor.";
    loginError.hidden = false;
    loginButton.disabled = true;
    return;
  }

  // ---------- mensajes ----------

  function showToast(message) {
    toast.textContent = message;
    toast.hidden = false;
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => { toast.hidden = true; }, 4500);
  }

  function run(action) {
    return action().catch(error => showToast(NewtinConnection.describeError(error)));
  }

  // ---------- pizarra ----------

  const board = NewtinBoard.create({
    canvas: $("canvas"),
    notesLayer: $("notes-layer"),
    viewport: $("viewport"),
    stage: $("stage"),
    sizer: $("board-sizer"),
    send: (method, ...args) => NewtinConnection.invoke(method, ...args),
    notify: showToast,
    describeError: error => NewtinConnection.describeError(error),
    onCategoriesChanged: renderCategories,
    onSelectionChanged: hasSelection => { deleteSelectionButton.disabled = !hasSelection; },
    onHistoryChanged: ({ canUndo, canRedo }) => {
      undoButton.disabled = !canUndo;
      redoButton.disabled = !canRedo;
    },
    onZoomChanged: zoom => { zoomReset.textContent = Math.round(zoom * 100) + "%"; }
  });

  // ---------- barra de herramientas ----------

  function markSelected(container, value) {
    for (const button of container.children)
      button.setAttribute("aria-pressed", String(button.dataset.color.toLowerCase() === value.toLowerCase()));
  }

  function buildPalette(container, colors, label, onPick) {
    for (const value of colors) {
      const button = document.createElement("button");
      button.type = "button";
      button.className = "swatch";
      button.style.background = value;
      button.dataset.color = value;
      button.setAttribute("aria-label", `${label} ${value}`);
      button.addEventListener("click", () => {
        onPick(value);
        markSelected(container, value);
      });
      container.appendChild(button);
    }
  }

  function selectTool(tool) {
    board.setTool(tool);
    for (const button of toolButtons) button.setAttribute("aria-pressed", String(button.dataset.tool === tool));

    colorOptions.hidden = !COLOR_TOOLS.includes(tool);
    widthOptions.hidden = !WIDTH_TOOLS.includes(tool);
    textOptions.hidden = tool !== "text";
    noteOptions.hidden = tool !== "note";
  }

  buildPalette(colorPalette, PEN_COLORS, "Color", value => {
    board.setColor(value);
    customColor.value = value.toLowerCase();
  });
  buildPalette(notePalette, NOTE_COLORS, "Color de nota", value => board.setNoteColor(value));
  markSelected(colorPalette, PEN_COLORS[0]);
  markSelected(notePalette, NOTE_COLORS[0]);
  board.setColor(PEN_COLORS[0]);
  board.setNoteColor(NOTE_COLORS[0]);

  for (const button of toolButtons) button.addEventListener("click", () => selectTool(button.dataset.tool));

  customColor.addEventListener("input", () => {
    board.setColor(customColor.value);
    markSelected(colorPalette, customColor.value);
  });

  widthInput.addEventListener("input", () => {
    widthValue.textContent = widthInput.value;
    board.setWidth(Number(widthInput.value));
  });

  fontSizeInput.addEventListener("input", () => {
    fontSizeValue.textContent = fontSizeInput.value;
    board.setFontSize(Number(fontSizeInput.value));
  });

  deleteSelectionButton.addEventListener("click", () => board.deleteSelected());
  undoButton.addEventListener("click", () => board.undo());
  redoButton.addEventListener("click", () => board.redo());
  $("zoom-in").addEventListener("click", () => board.zoomIn());
  $("zoom-out").addEventListener("click", () => board.zoomOut());
  zoomReset.addEventListener("click", () => board.resetZoom());
  $("zoom-fit").addEventListener("click", () => board.fitToWindow());

  // ---------- participantes y categorías ----------

  function showClients(info) {
    clientsCount.textContent = String(info.count);
    clientsList.replaceChildren(...info.names.map(name => {
      const item = document.createElement("li");
      item.textContent = name;
      return item;
    }));
  }

  function categoryRow(category, activeId) {
    const item = document.createElement("li");
    item.className = "category-row";

    const choose = document.createElement("button");
    choose.type = "button";
    choose.className = "category-name";
    choose.title = "Usar para las notas nuevas";
    choose.setAttribute("aria-pressed", String(category.id === activeId));
    if (category.color) {
      const dot = document.createElement("span");
      dot.className = "swatch-dot";
      dot.style.background = category.color;
      choose.appendChild(dot);
    }
    choose.appendChild(document.createTextNode(category.name));
    choose.addEventListener("click", () => board.setActiveCategory(category.id));
    item.appendChild(choose);

    if (category.id) {
      const color = document.createElement("input");
      color.type = "color";
      color.value = category.color.toLowerCase();
      color.setAttribute("aria-label", `Color de la categoría ${category.name}`);
      color.addEventListener("change", () =>
        run(() => NewtinConnection.invoke("UpdateCategory", { id: category.id, color: color.value })));
      item.appendChild(color);

      const remove = document.createElement("button");
      remove.type = "button";
      remove.className = "remove";
      remove.textContent = "×";
      remove.title = "Eliminar categoría";
      remove.setAttribute("aria-label", `Eliminar la categoría ${category.name}`);
      remove.addEventListener("click", async () => {
  const confirmed = await showDeleteCategoryModal(category.name);

  if (!confirmed) return;
          run(() => NewtinConnection.invoke("DeleteCategory", category.id));
      });
      item.appendChild(remove);
    }

    return item;
  }

  function renderCategories(list, activeId) {
    categoriesList.replaceChildren(
      categoryRow({ id: "", name: "Sin categoría", color: null }, activeId),
      ...list.map(category => categoryRow(category, activeId)));
  }

  categoryForm.addEventListener("submit", event => {
    event.preventDefault();
    const name = categoryName.value.trim();
    if (!name) {
      showToast("Escribe un nombre para la categoría.");
      return;
    }

    run(() => NewtinConnection.invoke("CreateCategory",
      { id: NewtinBoard.randomId("c"), name, color: categoryColor.value }))
      .then(() => { categoryName.value = ""; });
  });

  // ---------- conexión ----------

  function setStatus(kind, text) {
    statusPill.dataset.kind = kind;
    statusText.textContent = text;

    const online = kind === "ok";
    board.setEnabled(online);
    $("export-button").disabled = !online;

    banner.hidden = online;
    if (kind === "warn") {
      bannerText.textContent = `${text} Tus cambios no se enviarán hasta volver a conectar.`;
      reconnectButton.hidden = true;
    } else if (kind === "error") {
      bannerText.textContent = text;
      reconnectButton.hidden = false;
    }
  }

  function connectNow(name, code) {
    return NewtinConnection.connect({
      name,
      code,
      onStatus: setStatus,
      onSnapshot: snapshot => board.loadSnapshot(snapshot),
      onClients: showClients,
      onEvent: (eventName, payload) => board.applyEvent(eventName, payload)
    });
  }

  function showLogin(message) {
    appRoot.hidden = true;
    login.hidden = false;
    loginError.textContent = message ?? "";
    loginError.hidden = !message;
  }

  loginForm.addEventListener("submit", async event => {
    event.preventDefault();
    loginError.hidden = true;
    loginButton.disabled = true;

    const name = nameInput.value.trim();
    const code = codeInput.value.trim();

    try {
      await connectNow(name, code);
      session = { name, code };
      try { localStorage.setItem("newtin.name", name); } catch { /* el almacenamiento puede estar desactivado */ }
      login.hidden = true;
      appRoot.hidden = false;
      board.fitToWindow();
    } catch (error) {
      await NewtinConnection.disconnect();
      loginError.textContent = NewtinConnection.describeError(error);
      loginError.hidden = false;
    } finally {
      loginButton.disabled = false;
    }
  });

  reconnectButton.addEventListener("click", async () => {
    if (!session) return showLogin();
    reconnectButton.disabled = true;
    try {
      await connectNow(session.name, session.code);
    } catch (error) {
      await NewtinConnection.disconnect();
      showLogin(NewtinConnection.describeError(error));
    } finally {
      reconnectButton.disabled = false;
    }
  });

  $("leave-button").addEventListener("click", async () => {
    await NewtinConnection.disconnect();
    session = null;
    board.loadSnapshot({ elements: [], categories: [] });
    board.setEnabled(false);
    showLogin();
  });


$("clear-button").addEventListener("click", async () => {
    const confirmed = await showClearModal();

    if (!confirmed) return;

    run(() => NewtinConnection.invoke("ClearBoard"));
});

  // ---------- exportar ----------

  NewtinExport.init({
    openButton: $("export-button"),
    modal: $("export-modal"),
    getCode: () => session?.code ?? null,
    notify: showToast
});

  // ---------- inicio ----------

  try { nameInput.value = localStorage.getItem("newtin.name") ?? ""; } catch { /* sin almacenamiento */ }

  // El enlace puede traer #code=ABC123 (rellena el código) y, para la pizarra incrustada en Newtin,
  // &name=Equipo&auto=1 (entra directamente). Todo se borra de la barra de direcciones.
  const params = new URLSearchParams(location.hash.slice(1));
  const linkCode = (params.get("code") ?? "").replace(/[^A-Za-z0-9]/g, "");
  const linkName = (params.get("name") ?? "").trim();
  const autoJoin = params.get("auto") === "1";

  if (linkCode) codeInput.value = linkCode;
  if (linkName) nameInput.value = linkName;
  if (location.hash) history.replaceState(null, "", location.pathname);

  selectTool("pen");
  renderCategories([], "");

  if (autoJoin && linkCode && linkName) loginForm.requestSubmit();
  else (nameInput.value ? codeInput : nameInput).focus();

  
function showClearModal() {
    const modal = document.getElementById("clear-modal");
    const cancelButton = document.getElementById("cancel-clear");
    const confirmButton = document.getElementById("confirm-clear");
    const backdrop = modal.querySelector(".modal-backdrop");

    return new Promise((resolve) => {
        const previousFocus = document.activeElement;

        function finish(result) {
            modal.hidden = true;

            cancelButton.removeEventListener("click", onCancel);
            confirmButton.removeEventListener("click", onConfirm);
            backdrop.removeEventListener("click", onBackdrop);
            document.removeEventListener("keydown", onKeyDown);

            if (previousFocus && typeof previousFocus.focus === "function") {
                previousFocus.focus();
            }

            resolve(result);
        }

        function onCancel() {
            finish(false);
        }

        function onConfirm() {
            finish(true);
        }

        function onBackdrop() {
            finish(false);
        }

        function onKeyDown(event) {
            if (event.key === "Escape") {
                finish(false);
            }
        }

        cancelButton.addEventListener("click", onCancel);
        confirmButton.addEventListener("click", onConfirm);
        backdrop.addEventListener("click", onBackdrop);
        document.addEventListener("keydown", onKeyDown);

        modal.hidden = false;
        cancelButton.focus();
    });
}


function showDeleteCategoryModal(categoryName) {
  const modal = document.getElementById("delete-category-modal");
  const description = document.getElementById("delete-category-description");
  const cancelButton = document.getElementById("cancel-delete-category");
  const confirmButton = document.getElementById("confirm-delete-category");
  const backdrop = modal.querySelector(".modal-backdrop");

  description.textContent =
    `¿Eliminar la categoría «${categoryName}»? Las notas quedarán sin categoría.`;

  return new Promise((resolve) => {
    const previousFocus = document.activeElement;
    let finished = false;

    function finish(result) {
      if (finished) return;
      finished = true;

      modal.hidden = true;

      cancelButton.removeEventListener("click", onCancel);
      confirmButton.removeEventListener("click", onConfirm);
      backdrop.removeEventListener("click", onCancel);
      document.removeEventListener("keydown", onKeyDown);

      if (previousFocus && typeof previousFocus.focus === "function") {
        previousFocus.focus();
      }

      resolve(result);
    }

    function onCancel() {
      finish(false);
    }

    function onConfirm() {
      finish(true);
    }

    function onKeyDown(event) {
      if (event.key === "Escape") {
        finish(false);
      }
    }

    cancelButton.addEventListener("click", onCancel);
    confirmButton.addEventListener("click", onConfirm);
    backdrop.addEventListener("click", onCancel);
    document.addEventListener("keydown", onKeyDown);

    modal.hidden = false;
    cancelButton.focus();
  });
}


})();