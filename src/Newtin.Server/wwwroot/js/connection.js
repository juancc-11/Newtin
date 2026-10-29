"use strict";

/*
 * Conexión con el servidor de la pizarra (SignalR). Sin dependencias externas: usa js/signalr.min.js local.
 * Los cambios llegan con el estado oficial del servidor; se aplican por id y versión, así un evento
 * repetido nunca duplica un elemento.
 */
const NewtinConnection = (() => {
  const HUB_URL = "/hub/whiteboard";
  const SERVER_EVENTS = [
    "ElementCreated", "ElementUpdated", "ElementMoved", "ElementDeleted",
    "CategoryCreated", "CategoryUpdated", "CategoryDeleted",
    "BoardCleared", "BoardReplaced", "StrokeProgress"
  ];

  let connection = null;

  async function connect({ name, code, onStatus, onSnapshot, onClients, onEvent }) {
    await disconnect();

    connection = new signalR.HubConnectionBuilder()
      .withUrl(HUB_URL, { accessTokenFactory: () => code })
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 15000])
      .build();

    for (const eventName of SERVER_EVENTS) {
      connection.on(eventName, payload => onEvent(eventName, payload));
    }
    connection.on("ClientsChanged", info => onClients(info));

    // Tras una reconexión el servidor ve una conexión nueva: hay que identificarse otra vez y recibir el estado actual.
    const join = async () => {
      const result = await connection.invoke("Join", name);
      onSnapshot(result.snapshot);
      onClients(result.clients);
    };

    connection.onreconnecting(() => onStatus("warn", "Conexión perdida. Reconectando…"));
    connection.onreconnected(async () => {
      try {
        await join();
        onStatus("ok", "Reconectado");
      } catch (error) {
        onStatus("error", describeError(error));
      }
    });
    connection.onclose(() => onStatus("error", "Desconectado del servidor."));

    onStatus("warn", "Conectando…");
    await connection.start();
    await join();
    onStatus("ok", "Conectado");
  }

  async function disconnect() {
    if (!connection) return;
    const current = connection;
    connection = null;
    try { await current.stop(); } catch { /* ya estaba cerrada */ }
  }

  function invoke(method, ...args) {
    if (!connection) return Promise.reject(new Error("No hay conexión con el servidor."));
    return connection.invoke(method, ...args);
  }

  function describeError(error) {
    const text = String(error && error.message ? error.message : error);
    if (text.includes("401")) return "Código de acceso incorrecto.";
    if (text.includes("429")) return "Demasiados intentos fallidos. Espera unos minutos.";
    if (text.includes("HubException")) return text.replace(/^.*HubException:\s*/, "");
    if (text.includes("Connected' State")) return "Sin conexión con el servidor. Espera a que se restablezca.";
    return "No se pudo conectar con el servidor. Revisa la dirección y que el servidor esté activo.";
  }

  return { connect, disconnect, invoke, describeError };
})();