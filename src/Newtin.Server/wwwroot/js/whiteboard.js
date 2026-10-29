"use strict";

/*
 * Pizarra: lienzo (Canvas API) con trazos y textos, notas (elementos DOM) y categorías.
 * No conoce SignalR: recibe send(método, ...args), que devuelve una promesa.
 * El texto de los usuarios se asigna solo con textContent o value, nunca como marcado.
 * Los cambios llegan con el estado oficial del servidor y se aplican por id y versión,
 * así que un mismo evento repetido nunca duplica un elemento.
 */
const NewtinBoard = (() => {
  const BOARD_WIDTH = 2400;
  const BOARD_HEIGHT = 1500;
  const NOTE_WIDTH = 210;    // debe coincidir con .note en styles.css
  const NOTE_HEIGHT = 150;
  const MAX_POINTS = 5000;
  const MAX_CHUNK = 150;
  const MIN_POINT_DISTANCE = 1.5;
  const SEND_INTERVAL_MS = 60;
  const SAVE_DELAY_MS = 500;
  const PREVIEW_TIMEOUT_MS = 8000;
  const SETTLE_TIMEOUT_MS = 2000;
  const ELLIPSE_SEGMENTS = 48;
  const ARROW_ANGLE = Math.PI / 7;
  const MIN_ZOOM = 0.25;
  const MAX_ZOOM = 3;
  const ZOOM_STEP = 1.25;
  const WHEEL_STEP = 1.1;
  const MAX_HISTORY = 100;
  const LINE_HEIGHT = 1.25;
  const DEFAULT_FONT_SIZE = 24;
  const ERASER_TOLERANCE = 8;
  const TEXT_FONT = '"Segoe UI", system-ui, sans-serif';

  const round = value => Math.round(value * 10) / 10;
  const clamp = (value, min, max) => Math.min(max, Math.max(min, value));

  // crypto.randomUUID solo existe en páginas seguras (HTTPS o localhost);
  // getRandomValues funciona también con HTTP dentro de la red local.
  function randomId(prefix) {
    const bytes = new Uint8Array(12);
    crypto.getRandomValues(bytes);
    return prefix + "-" + Array.from(bytes, byte => byte.toString(16).padStart(2, "0")).join("");
  }

  function textColorFor(hex) {
    const value = /^#[0-9a-f]{6}$/i.test(hex) ? hex : "#FFF1A8";
    const r = parseInt(value.slice(1, 3), 16);
    const g = parseInt(value.slice(3, 5), 16);
    const b = parseInt(value.slice(5, 7), 16);
    return 0.299 * r + 0.587 * g + 0.114 * b > 150 ? "#17131F" : "#FFFFFF";
  }

  function distanceToSegment(p, a, b) {
    const dx = b.x - a.x;
    const dy = b.y - a.y;
    const lengthSquared = dx * dx + dy * dy;
    const t = lengthSquared === 0 ? 0 : clamp(((p.x - a.x) * dx + (p.y - a.y) * dy) / lengthSquared, 0, 1);
    return Math.hypot(p.x - (a.x + t * dx), p.y - (a.y + t * dy));
  }

  function rawBounds(points) {
    let minX = Infinity, minY = Infinity, maxX = -Infinity, maxY = -Infinity;
    for (const point of points) {
      minX = Math.min(minX, point.x);
      minY = Math.min(minY, point.y);
      maxX = Math.max(maxX, point.x);
      maxY = Math.max(maxY, point.y);
    }
    return { minX, minY, maxX, maxY };
  }

  // Líneas, flechas, rectángulos y elipses se guardan como trazos: el servidor no necesita saber de formas.
  function shapePoints(kind, a, b, strokeWidth) {
    if (kind === "line") return [{ x: a.x, y: a.y }, { x: b.x, y: b.y }];

    if (kind === "arrow") {
      const angle = Math.atan2(b.y - a.y, b.x - a.x);
      const head = Math.max(14, strokeWidth * 4);
      return [
        { x: a.x, y: a.y },
        { x: b.x, y: b.y },
        { x: b.x - head * Math.cos(angle - ARROW_ANGLE), y: b.y - head * Math.sin(angle - ARROW_ANGLE) },
        { x: b.x, y: b.y },
        { x: b.x - head * Math.cos(angle + ARROW_ANGLE), y: b.y - head * Math.sin(angle + ARROW_ANGLE) }
      ];
    }

    if (kind === "rect") {
      return [
        { x: a.x, y: a.y }, { x: b.x, y: a.y }, { x: b.x, y: b.y },
        { x: a.x, y: b.y }, { x: a.x, y: a.y }
      ];
    }

    const cx = (a.x + b.x) / 2;
    const cy = (a.y + b.y) / 2;
    const rx = Math.abs(b.x - a.x) / 2;
    const ry = Math.abs(b.y - a.y) / 2;
    const points = [];
    for (let i = 0; i <= ELLIPSE_SEGMENTS; i++) {
      const angle = (i / ELLIPSE_SEGMENTS) * 2 * Math.PI;
      points.push({ x: cx + rx * Math.cos(angle), y: cy + ry * Math.sin(angle) });
    }
    return points;
  }

  function hubError(message) {
    return new Error("HubException: " + message);
  }

  function create({
    canvas, notesLayer, viewport, stage, sizer,
    send, notify, describeError,
    onCategoriesChanged, onSelectionChanged, onHistoryChanged, onZoomChanged
  }) {
    const ctx = canvas.getContext("2d");
    canvas.width = BOARD_WIDTH;
    canvas.height = BOARD_HEIGHT;

    const elements = new Map();        // id -> elemento oficial del servidor
    const categories = new Map();      // id -> categoría
    const remotePreviews = new Map();  // id -> trazo ajeno en curso (no guardado)
    const pendingStrokes = new Map();  // id -> trazo mío enviado, a la espera de la confirmación
    const noteNodes = new Map();       // id -> elementos DOM de la nota
    const overrides = new Map();       // id -> posición local mientras arrastro
    const hiddenIds = new Set();       // elementos que el borrador ya quitó de mi vista
    const saveTimers = new Map();
    const undoStack = [];
    const redoStack = [];

    let tool = "pen";
    let color = "#17131F";
    let width = 4;
    let fontSize = 28;
    let noteColor = "#FFF1A8";
    let activeCategoryId = "";
    let enabled = false;
    let selectedId = null;
    let pendingFocusId = null;
    let drawing = null;
    let itemDrag = null;
    let noteDrag = null;
    let eraser = null;
    let panning = null;
    let textClick = null;
    let editor = null;
    let spaceDown = false;
    let historyBusy = false;
    let redrawQueued = false;
    let zoom = 1;

    // ---------- utilidades ----------

    function report(error) {
      notify(describeError(error));
    }

    function toBoard(event) {
      // getBoundingClientRect ya incluye el zoom, así que la conversión es la misma con cualquier escala.
      const rect = canvas.getBoundingClientRect();
      const x = (event.clientX - rect.left) * (BOARD_WIDTH / rect.width);
      const y = (event.clientY - rect.top) * (BOARD_HEIGHT / rect.height);
      return { x: clamp(x, 0, BOARD_WIDTH), y: clamp(y, 0, BOARD_HEIGHT) };
    }

    function positionOf(element) {
      return overrides.get(element.id) ?? { x: element.x, y: element.y };
    }

    function sortedCategories() {
      return [...categories.values()].sort((a, b) => a.name.localeCompare(b.name));
    }

    function canvasElementsInOrder() {
      return [...elements.values()]
        .filter(e => e.type === "stroke" || e.type === "text")
        .sort((a, b) => a.order - b.order);
    }

    function selectionChanged(id) {
      selectedId = id;
      onSelectionChanged(Boolean(id));
      requestRedraw();
    }

    function refresh(id) {
      const element = elements.get(id);
      if (!element) return;
      if (element.type === "note") renderNote(element);
      else requestRedraw();
    }

    function snapshotOf(element) {
      return {
        id: element.id,
        type: element.type,
        x: element.x,
        y: element.y,
        text: element.text,
        points: element.points,
        color: element.color,
        width: element.width,
        categoryId: element.categoryId ?? null
      };
    }

    function updateCursor() {
      canvas.style.cursor =
        panning ? "grabbing"
        : spaceDown || tool === "hand" ? "grab"
        : tool === "select" ? "default"
        : tool === "note" ? "copy"
        : tool === "text" ? "text"
        : "crosshair";
    }

    // ---------- texto: medidas y dibujo ----------

    function fontFor(size) {
      return `${size}px ${TEXT_FONT}`;
    }

    function textLines(element) {
      return (element.text ?? "").split("\n");
    }

    function textSize(element) {
      const size = element.width ?? DEFAULT_FONT_SIZE;
      ctx.font = fontFor(size);
      const lines = textLines(element);
      return {
        width: Math.max(...lines.map(line => ctx.measureText(line).width), 1),
        height: lines.length * size * LINE_HEIGHT
      };
    }

    function drawText(element, position) {
      const size = element.width ?? DEFAULT_FONT_SIZE;
      ctx.font = fontFor(size);
      ctx.fillStyle = element.color;
      ctx.textBaseline = "top";
      textLines(element).forEach((line, index) =>
        ctx.fillText(line, position.x, position.y + index * size * LINE_HEIGHT));
    }

    function boundsAt(element) {
      const position = positionOf(element);
      if (element.type === "text") {
        const size = textSize(element);
        return { x: position.x, y: position.y, w: size.width, h: size.height };
      }

      const box = rawBounds(element.points ?? []);
      return { x: box.minX + position.x, y: box.minY + position.y, w: box.maxX - box.minX, h: box.maxY - box.minY };
    }

    // Rango de posiciones permitido para que el elemento no salga de la pizarra.
    function dragLimits(element) {
      if (element.type === "text") {
        const size = textSize(element);
        return { minX: 0, maxX: BOARD_WIDTH - size.width, minY: 0, maxY: BOARD_HEIGHT - size.height };
      }

      const box = rawBounds(element.points ?? []);
      return { minX: -box.minX, maxX: BOARD_WIDTH - box.maxX, minY: -box.minY, maxY: BOARD_HEIGHT - box.maxY };
    }

    // ---------- dibujo ----------

    function requestRedraw() {
      if (redrawQueued) return;
      redrawQueued = true;
      requestAnimationFrame(redraw);
    }

    function drawStroke(points, strokeColor, strokeWidth, offset) {
      if (points.length === 0) return;

      if (points.length === 1) {
        ctx.fillStyle = strokeColor;
        ctx.beginPath();
        ctx.arc(points[0].x + offset.x, points[0].y + offset.y, strokeWidth / 2, 0, Math.PI * 2);
        ctx.fill();
        return;
      }

      ctx.strokeStyle = strokeColor;
      ctx.lineWidth = strokeWidth;
      ctx.beginPath();
      ctx.moveTo(points[0].x + offset.x, points[0].y + offset.y);
      for (let i = 1; i < points.length; i++) ctx.lineTo(points[i].x + offset.x, points[i].y + offset.y);
      ctx.stroke();
    }

    function redraw() {
      redrawQueued = false;
      ctx.clearRect(0, 0, BOARD_WIDTH, BOARD_HEIGHT);
      ctx.lineCap = "round";
      ctx.lineJoin = "round";

      const none = { x: 0, y: 0 };
      for (const element of canvasElementsInOrder()) {
        if (hiddenIds.has(element.id) || (editor && editor.id === element.id)) continue;

        if (element.type === "stroke")
          drawStroke(element.points ?? [], element.color, element.width ?? 3, positionOf(element));
        else
          drawText(element, positionOf(element));
      }

      for (const pending of pendingStrokes.values())
        drawStroke(pending.points, pending.color, pending.width, none);
      for (const preview of remotePreviews.values())
        drawStroke(preview.points, preview.color, preview.width, none);
      if (drawing) drawStroke(drawing.points, drawing.color, drawing.width, none);

      const selected = selectedId ? elements.get(selectedId) : null;
      if (selected && selected.type !== "note" && !hiddenIds.has(selected.id)) {
        const box = boundsAt(selected);
        const pad = (selected.type === "stroke" ? (selected.width ?? 3) / 2 : 0) + 6;
        ctx.save();
        ctx.setLineDash([8, 6]);
        ctx.strokeStyle = "#A00082";
        ctx.lineWidth = 2;
        ctx.strokeRect(box.x - pad, box.y - pad, box.w + pad * 2, box.h + pad * 2);
        ctx.restore();
      }
    }

    function addPenPoint(point) {
      const last = drawing.points[drawing.points.length - 1];
      if (Math.hypot(point.x - last.x, point.y - last.y) < MIN_POINT_DISTANCE) return;

      if (drawing.points.length >= MAX_POINTS) {
        if (!drawing.capped) {
          drawing.capped = true;
          notify("El trazo alcanzó el máximo de puntos. Suelta y continúa con un trazo nuevo.");
        }
        return;
      }
      drawing.points.push(point);
    }

    // Vista previa en vivo para los demás: solo los puntos nuevos desde el último envío.
    function flushProgress() {
      const now = performance.now();
      if (now - drawing.lastSent < SEND_INTERVAL_MS) return;
      if (drawing.points.length <= drawing.sentCount) return;

      const from = Math.max(drawing.sentCount - 1, 0);
      const to = Math.min(drawing.points.length, from + MAX_CHUNK);
      const chunk = drawing.points.slice(from, to).map(p => ({ x: round(p.x), y: round(p.y) }));

      drawing.sentCount = to;
      drawing.lastSent = now;
      send("StrokeProgress", { id: drawing.id, color: drawing.color, width: drawing.width, points: chunk })
        .catch(() => { /* una vista previa perdida no importa: el trazo final sí se confirma */ });
    }

    function commitDrawing() {
      const shape = drawing;
      drawing = null;

      if (shape.kind !== "pen" && Math.hypot(shape.end.x - shape.start.x, shape.end.y - shape.start.y) < 3) {
        requestRedraw();
        return;
      }

      const points = shape.points.map(p => ({ x: round(p.x), y: round(p.y) }));
      pendingStrokes.set(shape.id, { points, color: shape.color, width: shape.width });
      requestRedraw();

      send("CreateElement", { id: shape.id, type: "stroke", x: 0, y: 0, points, color: shape.color, width: shape.width })
        .then(() => pushHistory(createCommand(shape.id)))
        .catch(error => {
          pendingStrokes.delete(shape.id);
          requestRedraw();
          report(error);
        });
    }

    // ---------- selección, movimiento y borrador ----------

    function hitElement(element, point, extra) {
      if (element.type === "text") {
        const box = boundsAt(element);
        const margin = 4 + extra;
        return point.x >= box.x - margin && point.x <= box.x + box.w + margin
            && point.y >= box.y - margin && point.y <= box.y + box.h + margin;
      }

      const offset = positionOf(element);
      const tolerance = (element.width ?? 3) / 2 + 6 + extra;
      const shifted = (element.points ?? []).map(p => ({ x: p.x + offset.x, y: p.y + offset.y }));

      if (shifted.length === 1) return Math.hypot(point.x - shifted[0].x, point.y - shifted[0].y) <= tolerance;

      for (let i = 1; i < shifted.length; i++)
        if (distanceToSegment(point, shifted[i - 1], shifted[i]) <= tolerance) return true;
      return false;
    }

    function findAt(point, extra = 0) {
      const ordered = canvasElementsInOrder();
      for (let i = ordered.length - 1; i >= 0; i--) {
        if (hiddenIds.has(ordered[i].id)) continue;
        if (hitElement(ordered[i], point, extra)) return ordered[i];
      }
      return null;
    }

    function beginSelection(event, point) {
      const hit = findAt(point);
      selectionChanged(hit ? hit.id : null);
      if (!hit) return;

      const position = positionOf(hit);
      itemDrag = {
        id: hit.id,
        pointerId: event.pointerId,
        startX: point.x,
        startY: point.y,
        originX: position.x,
        originY: position.y,
        limits: dragLimits(hit),
        lastSent: 0,
        moved: false
      };
    }

    function moveItemDrag(point) {
      const drag = itemDrag;
      const x = round(clamp(drag.originX + point.x - drag.startX, drag.limits.minX, drag.limits.maxX));
      const y = round(clamp(drag.originY + point.y - drag.startY, drag.limits.minY, drag.limits.maxY));

      overrides.set(drag.id, { x, y, final: false });
      drag.moved = true;
      throttledMove(drag, x, y);
      requestRedraw();
    }

    function endItemDrag() {
      const drag = itemDrag;
      itemDrag = null;

      const override = overrides.get(drag.id);
      if (!drag.moved || !override || !elements.has(drag.id)) {
        overrides.delete(drag.id);
        requestRedraw();
        return;
      }
      finishMove(drag.id, override.x, override.y, { x: drag.originX, y: drag.originY });
    }

    function throttledMove(state, x, y) {
      const now = performance.now();
      if (now - state.lastSent < SEND_INTERVAL_MS) return;
      state.lastSent = now;
      send("MoveElement", state.id, x, y).catch(() => { /* el movimiento final sí se confirma */ });
    }

    // La posición local se mantiene hasta que llega la confirmación del servidor, para evitar parpadeos.
    function finishMove(id, x, y, from) {
      const element = elements.get(id);
      if (element && element.x === x && element.y === y) {
        overrides.delete(id);
        refresh(id);
        return;
      }

      overrides.set(id, { x, y, final: true });
      send("MoveElement", id, x, y)
        .then(() => pushHistory(moveCommand(id, from, { x, y })))
        .catch(error => {
          overrides.delete(id);
          refresh(id);
          report(error);
        });

      setTimeout(() => {
        const override = overrides.get(id);
        if (override && override.final) {
          overrides.delete(id);
          refresh(id);
        }
      }, SETTLE_TIMEOUT_MS);
    }

    function settleOverride(element) {
      const override = overrides.get(element.id);
      if (!override || !override.final) return;
      if (Math.abs(override.x - element.x) < 0.05 && Math.abs(override.y - element.y) < 0.05)
        overrides.delete(element.id);
    }

    // El borrador quita de mi vista lo que toca y lo borra en el servidor; una pasada es un solo "deshacer".
    function eraseAt(point) {
      const hit = findAt(point, ERASER_TOLERANCE);
      if (!hit) return;

      hiddenIds.add(hit.id);
      requestRedraw();

      const snapshot = snapshotOf(hit);
      const request = send("DeleteElement", hit.id);
      eraser.items.push({ snapshot, request });
      request.catch(error => {
        hiddenIds.delete(hit.id);
        requestRedraw();
        report(error);
      });
    }

    function endEraser() {
      const items = eraser.items;
      eraser = null;
      if (items.length === 0) return;

      Promise.allSettled(items.map(item => item.request)).then(results => {
        const deleted = items.filter((_, index) => results[index].status === "fulfilled").map(item => item.snapshot);
        if (deleted.length > 0) pushHistory(deleteCommand(deleted));
      });
    }

    // ---------- zoom y desplazamiento ----------

    function applyZoom() {
      stage.style.transform = `scale(${zoom})`;
      sizer.style.width = BOARD_WIDTH * zoom + "px";
      sizer.style.height = BOARD_HEIGHT * zoom + "px";
      onZoomChanged(zoom);
    }

    // Mantiene bajo el ancla (el puntero o el centro) el mismo punto de la pizarra.
    function setZoom(next, anchorX, anchorY) {
      const rect = viewport.getBoundingClientRect();
      const ax = anchorX ?? rect.left + rect.width / 2;
      const ay = anchorY ?? rect.top + rect.height / 2;

      const boardX = (viewport.scrollLeft + ax - rect.left) / zoom;
      const boardY = (viewport.scrollTop + ay - rect.top) / zoom;

      zoom = clamp(next, MIN_ZOOM, MAX_ZOOM);
      applyZoom();

      viewport.scrollLeft = boardX * zoom - (ax - rect.left);
      viewport.scrollTop = boardY * zoom - (ay - rect.top);
    }

    function fitToWindow() {
      const fit = Math.min(viewport.clientWidth / BOARD_WIDTH, viewport.clientHeight / BOARD_HEIGHT);
      setZoom(fit);
      viewport.scrollLeft = 0;
      viewport.scrollTop = 0;
    }

    viewport.addEventListener("wheel", event => {
      if (!event.ctrlKey) return;
      event.preventDefault();
      setZoom(zoom * (event.deltaY < 0 ? WHEEL_STEP : 1 / WHEEL_STEP), event.clientX, event.clientY);
    }, { passive: false });

    function startPan(event) {
      canvas.setPointerCapture(event.pointerId);
      panning = {
        pointerId: event.pointerId,
        x: event.clientX,
        y: event.clientY,
        left: viewport.scrollLeft,
        top: viewport.scrollTop
      };
      updateCursor();
    }

    function movePan(event) {
      viewport.scrollLeft = panning.left - (event.clientX - panning.x);
      viewport.scrollTop = panning.top - (event.clientY - panning.y);
    }

    function endPan() {
      panning = null;
      updateCursor();
    }

    // ---------- texto libre ----------

    function sizeEditor(area, size) {
      ctx.font = fontFor(size);
      const longest = Math.max(...area.value.split("\n").map(line => ctx.measureText(line).width), 0);
      area.style.width = Math.max(160, longest + 24) + "px";
      area.style.height = "auto";
      area.style.height = area.scrollHeight + "px";
    }

    function openEditor({ id, x, y, size, textColor, text }) {
      closeEditor(true);

      const area = document.createElement("textarea");
      area.className = "text-editor";
      area.maxLength = 2000;
      area.value = text;
      area.setAttribute("aria-label", "Texto de la pizarra");
      area.style.left = (x - 3) + "px";
      area.style.top = (y - 3) + "px";
      area.style.fontSize = size + "px";
      area.style.color = textColor;

      editor = { area, id, x, y, size, textColor, original: text };
      stage.appendChild(area);
      sizeEditor(area, size);
      area.focus();
      requestRedraw();

      area.addEventListener("input", () => sizeEditor(area, size));
      area.addEventListener("blur", () => {
        if (editor && editor.area === area) closeEditor(true);
      });
      area.addEventListener("keydown", event => {
        if (event.key === "Escape") {
          event.preventDefault();
          closeEditor(false);
        } else if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) {
          event.preventDefault();
          closeEditor(true);
        }
      });
    }

    function closeEditor(commit) {
      if (!editor) return;

      const { area, id, x, y, size, textColor, original } = editor;
      const text = area.value.trimEnd();
      editor = null;
      area.remove();
      requestRedraw();

      // Un texto vacío o sin cambios no se envía; para quitar un texto se usa «Seleccionar» y Supr.
      if (!commit || !enabled || text.length === 0 || text === original.trimEnd()) return;

      if (id) updateText(id, text);
      else createText(x, y, size, textColor, text);
    }

    function openEditorFor(element) {
      const position = positionOf(element);
      openEditor({
        id: element.id,
        x: position.x,
        y: position.y,
        size: element.width ?? DEFAULT_FONT_SIZE,
        textColor: element.color,
        text: element.text ?? ""
      });
    }

    function handleTextClick(point) {
      const hit = findAt(point);
      if (hit && hit.type === "text") {
        openEditorFor(hit);
        return;
      }

      openEditor({
        id: null,
        x: clamp(point.x, 0, BOARD_WIDTH - 40),
        y: clamp(point.y, 0, BOARD_HEIGHT - 40),
        size: fontSize,
        textColor: color,
        text: ""
      });
    }

    function createText(x, y, size, textColor, text) {
      const id = randomId("t");
      send("CreateElement", { id, type: "text", x: round(x), y: round(y), text, color: textColor, width: size })
        .then(() => pushHistory(createCommand(id)))
        .catch(report);
    }

    function updateText(id, text) {
      const element = elements.get(id);
      if (!element) return;

      const before = { text: element.text ?? "", categoryId: element.categoryId ?? null };
      const after = { text, categoryId: before.categoryId };
      send("UpdateElement", { id, type: element.type, x: element.x, y: element.y, ...after })
        .then(() => pushHistory(updateCommand(id, element.type, before, after)))
        .catch(report);
    }

    // ---------- eventos del lienzo ----------

    function onPointerDown(event) {
      const wantsPan = tool === "hand" || spaceDown || (event.pointerType === "mouse" && event.button === 1);
      if (wantsPan) {
        if (panning) return;
        event.preventDefault();
        startPan(event);
        return;
      }

      if (!enabled || (event.pointerType === "mouse" && event.button !== 0)) return;
      if (drawing || itemDrag || eraser || panning) return;

      const point = toBoard(event);

      if (tool === "note") {
        createNote(point.x, point.y);
        return;
      }

      canvas.setPointerCapture(event.pointerId);

      // El editor de texto se abre al soltar, para que el clic no le quite el foco.
      if (tool === "text") {
        textClick = { pointerId: event.pointerId, point };
        return;
      }

      if (tool === "select") {
        beginSelection(event, point);
        return;
      }

      if (tool === "eraser") {
        eraser = { pointerId: event.pointerId, items: [] };
        eraseAt(point);
        return;
      }

      drawing = tool === "pen"
        ? { kind: "pen", id: randomId("s"), pointerId: event.pointerId, points: [point], color, width, sentCount: 0, lastSent: 0, capped: false }
        : { kind: tool, id: randomId("s"), pointerId: event.pointerId, start: point, end: point, points: shapePoints(tool, point, point, width), color, width };
      requestRedraw();
    }

    function onPointerMove(event) {
      if (panning && event.pointerId === panning.pointerId) {
        movePan(event);
      } else if (drawing && event.pointerId === drawing.pointerId) {
        const point = toBoard(event);
        if (drawing.kind === "pen") {
          addPenPoint(point);
          flushProgress();
        } else {
          drawing.end = point;
          drawing.points = shapePoints(drawing.kind, drawing.start, point, drawing.width);
        }
        requestRedraw();
      } else if (itemDrag && event.pointerId === itemDrag.pointerId) {
        moveItemDrag(toBoard(event));
      } else if (eraser && event.pointerId === eraser.pointerId) {
        eraseAt(toBoard(event));
      }
    }

    function onPointerUp(event) {
      if (panning && event.pointerId === panning.pointerId) {
        endPan();
      } else if (drawing && event.pointerId === drawing.pointerId) {
        commitDrawing();
      } else if (itemDrag && event.pointerId === itemDrag.pointerId) {
        endItemDrag();
      } else if (eraser && event.pointerId === eraser.pointerId) {
        endEraser();
      } else if (textClick && event.pointerId === textClick.pointerId) {
        const click = textClick;
        textClick = null;
        handleTextClick(click.point);
      }
    }

    canvas.addEventListener("pointerdown", onPointerDown);
    canvas.addEventListener("pointermove", onPointerMove);
    canvas.addEventListener("pointerup", onPointerUp);
    canvas.addEventListener("pointercancel", onPointerUp);

    canvas.addEventListener("dblclick", event => {
      if (!enabled || tool !== "select") return;
      const hit = findAt(toBoard(event));
      if (hit && hit.type === "text") openEditorFor(hit);
    });

    function isTyping(target) {
      return target instanceof HTMLElement && ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName);
    }

    document.addEventListener("keydown", event => {
      if (isTyping(event.target)) return;

      if ((event.ctrlKey || event.metaKey) && !event.altKey) {
        const key = event.key.toLowerCase();
        if (key === "z") {
          event.preventDefault();
          if (event.shiftKey) redo(); else undo();
        } else if (key === "y") {
          event.preventDefault();
          redo();
        }
        return;
      }

      const onButton = event.target instanceof HTMLElement && event.target.tagName === "BUTTON";
      if (event.key === " " && !spaceDown && !onButton) {
        event.preventDefault();
        spaceDown = true;
        updateCursor();
      } else if (selectedId && (event.key === "Delete" || event.key === "Backspace")) {
        event.preventDefault();
        deleteSelected();
      } else if (selectedId && event.key === "Escape") {
        selectionChanged(null);
      }
    });

    document.addEventListener("keyup", event => {
      if (event.key === " ") {
        spaceDown = false;
        updateCursor();
      }
    });

    window.addEventListener("blur", () => {
      spaceDown = false;
      updateCursor();
    });

    // ---------- historial (deshacer y rehacer, solo de mis acciones) ----------

    function historyChanged() {
      onHistoryChanged({
        canUndo: enabled && undoStack.length > 0,
        canRedo: enabled && redoStack.length > 0
      });
    }

    function pushHistory(command) {
      undoStack.push(command);
      if (undoStack.length > MAX_HISTORY) undoStack.shift();
      redoStack.length = 0;
      historyChanged();
    }

    function clearHistory() {
      undoStack.length = 0;
      redoStack.length = 0;
      historyChanged();
    }

    async function runHistory(from, to, direction) {
      if (historyBusy || !enabled || from.length === 0) return;

      historyBusy = true;
      const command = from.pop();
      try {
        await command[direction]();
        to.push(command);
      } catch (error) {
        report(error);   // la acción ya no se puede aplicar (por ejemplo, otra persona borró el elemento)
      } finally {
        historyBusy = false;
        historyChanged();
      }
    }

    function undo() { return runHistory(undoStack, redoStack, "undo"); }
    function redo() { return runHistory(redoStack, undoStack, "redo"); }

    // Un elemento borrado se recrea con el mismo id; si su categoría ya no existe, se recrea sin ella.
    async function recreate(input) {
      try {
        await send("CreateElement", input);
      } catch (error) {
        if (input.categoryId && /categor/i.test(String(error && error.message))) {
          await send("CreateElement", { ...input, categoryId: null });
          return;
        }
        throw error;
      }
    }

    function createCommand(id) {
      let snapshot = null;
      return {
        async undo() {
          const element = elements.get(id);
          if (!element) throw hubError("El elemento ya no existe.");
          snapshot = snapshotOf(element);
          await send("DeleteElement", id);
        },
        async redo() {
          await recreate(snapshot);
        }
      };
    }

    function deleteCommand(snapshots) {
      return {
        async undo() { for (const snapshot of snapshots) await recreate(snapshot); },
        async redo() { for (const snapshot of snapshots) await send("DeleteElement", snapshot.id); }
      };
    }

    function moveCommand(id, from, to) {
      const move = async target => {
        if (!elements.has(id)) throw hubError("El elemento ya no existe.");
        await send("MoveElement", id, target.x, target.y);
      };
      return { undo: () => move(from), redo: () => move(to) };
    }

    function updateCommand(id, type, before, after) {
      const apply = async values => {
        const element = elements.get(id);
        if (!element) throw hubError("El elemento ya no existe.");
        await send("UpdateElement", { id, type, x: element.x, y: element.y, ...values });
      };
      return { undo: () => apply(before), redo: () => apply(after) };
    }

    // ---------- notas ----------

    function createNote(x, y) {
      const id = randomId("n");
      pendingFocusId = id;

      send("CreateElement", {
        id,
        type: "note",
        x: round(clamp(x - 10, 0, BOARD_WIDTH - NOTE_WIDTH)),
        y: round(clamp(y - 10, 0, BOARD_HEIGHT - NOTE_HEIGHT)),
        text: "",
        color: noteColor,
        categoryId: activeCategoryId || null
      })
        .then(() => pushHistory(createCommand(id)))
        .catch(error => {
          pendingFocusId = null;
          report(error);
        });
    }

    function buildNote(id) {
      const root = document.createElement("div");
      root.className = "note";
      root.dataset.id = id;

      const header = document.createElement("div");
      header.className = "note-header";

      const label = document.createElement("span");
      label.className = "note-label";

      const remove = document.createElement("button");
      remove.type = "button";
      remove.className = "note-delete";
      remove.textContent = "×";
      remove.title = "Eliminar nota";
      remove.setAttribute("aria-label", "Eliminar nota");

      header.append(label, remove);

      const textarea = document.createElement("textarea");
      textarea.className = "note-text";
      textarea.maxLength = 2000;
      textarea.placeholder = "Escribe aquí…";
      textarea.setAttribute("aria-label", "Texto de la nota");

      const select = document.createElement("select");
      select.className = "note-category";
      select.setAttribute("aria-label", "Categoría de la nota");

      root.append(header, textarea, select);

      header.addEventListener("pointerdown", event => startNoteDrag(event, id, header));
      header.addEventListener("pointermove", moveNoteDrag);
      header.addEventListener("pointerup", endNoteDrag);
      header.addEventListener("pointercancel", endNoteDrag);
      remove.addEventListener("pointerdown", event => event.stopPropagation());
      remove.addEventListener("click", () => requestDeleteNote(id));
      textarea.addEventListener("input", () => scheduleSave(id));
      textarea.addEventListener("blur", () => saveText(id));
      select.addEventListener("change", () => changeNoteCategory(id, select.value));

      return { root, header, label, remove, textarea, select, signature: "" };
    }

    function fillCategorySelect(node, selectedCategoryId) {
      const list = sortedCategories();
      const signature = list.map(c => c.id + ":" + c.name).join("|");

      if (node.signature !== signature) {
        node.select.replaceChildren(new Option("Sin categoría", ""));
        for (const category of list) node.select.appendChild(new Option(category.name, category.id));
        node.signature = signature;
      }

      node.select.value = selectedCategoryId && categories.has(selectedCategoryId) ? selectedCategoryId : "";
    }

    function renderNote(element) {
      let node = noteNodes.get(element.id);
      if (!node) {
        node = buildNote(element.id);
        noteNodes.set(element.id, node);
        notesLayer.appendChild(node.root);
      }

      const position = positionOf(element);
      node.root.style.left = clamp(position.x, 0, BOARD_WIDTH - NOTE_WIDTH) + "px";
      node.root.style.top = clamp(position.y, 0, BOARD_HEIGHT - NOTE_HEIGHT) + "px";
      node.root.style.background = element.color;
      node.root.style.color = textColorFor(element.color);

      const category = element.categoryId ? categories.get(element.categoryId) : null;
      node.header.style.background = category ? category.color : "rgba(0, 0, 0, 0.12)";
      node.header.style.color = category ? textColorFor(category.color) : "inherit";
      node.label.textContent = category ? category.name : "Sin categoría";

      // No se pisa lo que la persona está escribiendo en este momento.
      if (document.activeElement !== node.textarea) node.textarea.value = element.text ?? "";

      fillCategorySelect(node, element.categoryId);

      node.textarea.readOnly = !enabled;
      node.select.disabled = !enabled;
      node.remove.disabled = !enabled;
      node.root.classList.toggle("offline", !enabled);

      if (pendingFocusId === element.id) {
        pendingFocusId = null;
        node.textarea.focus();
      }
    }

    function startNoteDrag(event, id, header) {
      if (!enabled || event.button !== 0 || noteDrag) return;
      const element = elements.get(id);
      if (!element) return;

      header.setPointerCapture(event.pointerId);
      const point = toBoard(event);
      const position = positionOf(element);

      noteDrag = {
        id,
        pointerId: event.pointerId,
        startX: point.x,
        startY: point.y,
        originX: position.x,
        originY: position.y,
        lastSent: 0,
        moved: false
      };
      event.preventDefault();
    }

    function moveNoteDrag(event) {
      if (!noteDrag || event.pointerId !== noteDrag.pointerId) return;
      const element = elements.get(noteDrag.id);
      if (!element) return;

      const point = toBoard(event);
      const x = round(clamp(noteDrag.originX + point.x - noteDrag.startX, 0, BOARD_WIDTH - NOTE_WIDTH));
      const y = round(clamp(noteDrag.originY + point.y - noteDrag.startY, 0, BOARD_HEIGHT - NOTE_HEIGHT));

      overrides.set(noteDrag.id, { x, y, final: false });
      noteDrag.moved = true;
      renderNote(element);
      throttledMove(noteDrag, x, y);
    }

    function endNoteDrag(event) {
      if (!noteDrag || event.pointerId !== noteDrag.pointerId) return;
      const drag = noteDrag;
      noteDrag = null;

      const override = overrides.get(drag.id);
      if (!drag.moved || !override || !elements.has(drag.id)) {
        overrides.delete(drag.id);
        return;
      }
      finishMove(drag.id, override.x, override.y, { x: drag.originX, y: drag.originY });
    }

    function scheduleSave(id) {
      clearTimeout(saveTimers.get(id));
      saveTimers.set(id, setTimeout(() => saveText(id), SAVE_DELAY_MS));
    }

    // UpdateElement reemplaza la categoría, así que siempre se envía la actual junto con el texto.
    function saveText(id) {
      clearTimeout(saveTimers.get(id));
      saveTimers.delete(id);

      const element = elements.get(id);
      const node = noteNodes.get(id);
      if (!enabled || !element || !node) return;

      const text = node.textarea.value;
      const previous = element.text ?? "";
      if (previous === text) return;

      const before = { text: previous, categoryId: element.categoryId ?? null };
      const after = { text, categoryId: before.categoryId };
      send("UpdateElement", { id, type: "note", x: element.x, y: element.y, ...after })
        .then(() => pushHistory(updateCommand(id, "note", before, after)))
        .catch(report);
    }

    function changeNoteCategory(id, categoryId) {
      const element = elements.get(id);
      if (!element) return;

      const before = { categoryId: element.categoryId ?? null };
      const after = { categoryId: categoryId || null };
      send("UpdateElement", { id, type: "note", x: element.x, y: element.y, ...after })
        .then(() => pushHistory(updateCommand(id, "note", before, after)))
        .catch(error => {
          refresh(id);
          report(error);
        });
    }

    function requestDeleteNote(id) {
      if (!enabled) return;
      const element = elements.get(id);
      if (!element) return;

      const node = noteNodes.get(id);
      const text = (node?.textarea.value || element.text || "").trim();
      if (text.length > 0 && !window.confirm("¿Eliminar esta nota para todos los participantes?")) return;

      const snapshot = snapshotOf(element);
      send("DeleteElement", id)
        .then(() => pushHistory(deleteCommand([snapshot])))
        .catch(report);
    }

    // ---------- estado recibido del servidor ----------

    function upsertElement(element) {
      const current = elements.get(element.id);
      if (current && element.version <= current.version) return;

      elements.set(element.id, element);
      settleOverride(element);

      if (element.type === "note") {
        renderNote(element);
      } else {
        pendingStrokes.delete(element.id);
        remotePreviews.delete(element.id);
        requestRedraw();
      }
    }

    function removeElement(id) {
      if (!elements.delete(id)) return;

      overrides.delete(id);
      hiddenIds.delete(id);
      clearTimeout(saveTimers.get(id));
      saveTimers.delete(id);
      if (editor && editor.id === id) closeEditor(false);

      const node = noteNodes.get(id);
      if (node) {
        node.root.remove();
        noteNodes.delete(id);
      }

      if (selectedId === id) selectionChanged(null);
      requestRedraw();
    }

    function clearElements() {
      closeEditor(false);
      elements.clear();
      overrides.clear();
      hiddenIds.clear();
      remotePreviews.clear();
      pendingStrokes.clear();
      noteNodes.clear();
      notesLayer.replaceChildren();
      for (const timer of saveTimers.values()) clearTimeout(timer);
      saveTimers.clear();
      clearHistory();
      if (selectedId) selectionChanged(null);
      requestRedraw();
    }

    function categoriesChanged() {
      if (activeCategoryId && !categories.has(activeCategoryId)) activeCategoryId = "";
      for (const element of elements.values()) if (element.type === "note") renderNote(element);
      onCategoriesChanged(sortedCategories(), activeCategoryId);
    }

    function applyProgress(progress) {
      if (elements.has(progress.id)) return;   // llegó tarde: el trazo ya está confirmado

      let preview = remotePreviews.get(progress.id);
      if (!preview) {
        if (remotePreviews.size >= 50) return;
        preview = { color: progress.color, width: progress.width, points: [], updatedAt: 0 };
        remotePreviews.set(progress.id, preview);
      }

      for (const point of progress.points) {
        const last = preview.points[preview.points.length - 1];
        if (last && last.x === point.x && last.y === point.y) continue;
        if (preview.points.length < MAX_POINTS) preview.points.push(point);
      }

      preview.updatedAt = Date.now();
      requestRedraw();
    }

    setInterval(() => {
      const limit = Date.now() - PREVIEW_TIMEOUT_MS;
      let removed = false;
      for (const [id, preview] of remotePreviews) {
        if (preview.updatedAt < limit) {
          remotePreviews.delete(id);
          removed = true;
        }
      }
      if (removed) requestRedraw();
    }, 2000);

    // ---------- API pública ----------

    function loadSnapshot(snapshot) {
      clearElements();
      drawing = null;
      itemDrag = null;
      noteDrag = null;
      eraser = null;

      categories.clear();
      for (const category of snapshot.categories) categories.set(category.id, category);
      for (const element of snapshot.elements) elements.set(element.id, element);
      for (const element of elements.values()) if (element.type === "note") renderNote(element);

      categoriesChanged();
      requestRedraw();
    }

    function applyEvent(name, payload) {
      switch (name) {
        case "ElementCreated":
        case "ElementUpdated":
        case "ElementMoved":
          upsertElement(payload);
          break;
        case "ElementDeleted":
          removeElement(payload);
          break;
        case "CategoryCreated":
        case "CategoryUpdated":
          categories.set(payload.id, payload);
          categoriesChanged();
          break;
        case "CategoryDeleted":
          categories.delete(payload);
          categoriesChanged();
          break;
        case "BoardCleared":
          clearElements();
          break;
        case "BoardReplaced":
          loadSnapshot(payload);
          break;
        case "StrokeProgress":
          applyProgress(payload);
          break;
        default:
          break;
      }
    }

    function setEnabled(value) {
      enabled = value;
      if (!value) {
        closeEditor(false);
        drawing = null;
        itemDrag = null;
        noteDrag = null;
        eraser = null;
      }
      for (const element of elements.values()) if (element.type === "note") renderNote(element);
      historyChanged();
      requestRedraw();
    }

    function setTool(value) {
      closeEditor(true);
      tool = value;
      updateCursor();
      if (value !== "select" && selectedId) selectionChanged(null);
    }

    function setActiveCategory(id) {
      activeCategoryId = id && categories.has(id) ? id : "";
      onCategoriesChanged(sortedCategories(), activeCategoryId);
    }

    function deleteSelected() {
      if (!enabled || !selectedId) return;

      const element = elements.get(selectedId);
      selectionChanged(null);
      if (!element) return;

      const snapshot = snapshotOf(element);
      send("DeleteElement", element.id)
        .then(() => pushHistory(deleteCommand([snapshot])))
        .catch(report);
    }

    setTool("pen");
    applyZoom();
    historyChanged();

    return {
      loadSnapshot,
      applyEvent,
      setEnabled,
      setTool,
      setColor: value => { color = value; },
      setWidth: value => { width = value; },
      setFontSize: value => { fontSize = value; },
      setNoteColor: value => { noteColor = value; },
      setActiveCategory,
      deleteSelected,
      getCategories: sortedCategories,
      undo,
      redo,
      zoomIn: () => setZoom(zoom * ZOOM_STEP),
      zoomOut: () => setZoom(zoom / ZOOM_STEP),
      resetZoom: () => setZoom(1),
      fitToWindow
    };
  }

  return { create, randomId };
})();