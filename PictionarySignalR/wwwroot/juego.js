let conexion = null;
let nombreJugador = "";
let colorActual = "#111827";
let tamanoActual = 5;
let estaDibujando = false;
let puedeDibujar = false;
let ultimoPunto = null;
let palabraDibujante = "";
let reporteEnviado = false;
let puedeEnviarMensaje = false;

const vistas = {
    entrada: document.getElementById("vistaEntrada"),
    espera: document.getElementById("vistaEspera"),
    juego: document.getElementById("vistaJuego"),
    final: document.getElementById("vistaFinal")
};

const txtNombre = document.getElementById("txtNombre");
const btnEntrar = document.getElementById("btnEntrar");
const mensajeEntrada = document.getElementById("mensajeEntrada");
const btnListo = document.getElementById("btnListo");
const listaEspera = document.getElementById("listaEspera");
const mensajeEspera = document.getElementById("mensajeEspera");
const tiempoEspera = document.getElementById("tiempoEspera");
const listaJugadores = document.getElementById("listaJugadores");
const textoTurno = document.getElementById("textoTurno");
const palabraRonda = document.getElementById("palabraRonda");
const tiempoRonda = document.getElementById("tiempoRonda");
const panelPalabra = document.getElementById("panelPalabra");
const txtPalabra = document.getElementById("txtPalabra");
const btnEnviarPalabra = document.getElementById("btnEnviarPalabra");
const herramientas = document.getElementById("herramientas");
const rangoTamano = document.getElementById("rangoTamano");
const btnLimpiar = document.getElementById("btnLimpiar");
const btnReportar = document.getElementById("btnReportar");
const mensajesChat = document.getElementById("mensajesChat");
const formChat = document.getElementById("formChat");
const txtMensaje = document.getElementById("txtMensaje");
const textoGanador = document.getElementById("textoGanador");
const listaFinal = document.getElementById("listaFinal");
const tiempoFinal = document.getElementById("tiempoFinal");
const pizarra = document.getElementById("pizarra");
const contexto = pizarra.getContext("2d");

if (window.lucide) {
    lucide.createIcons();
}

btnEntrar.addEventListener("click", entrarSala);
txtNombre.addEventListener("keydown", (e) => {
    if (e.key === "Enter") entrarSala();
});
btnListo.addEventListener("click", marcarListo);
btnEnviarPalabra.addEventListener("click", enviarPalabra);
btnLimpiar.addEventListener("click", limpiarPizarra);
btnReportar.addEventListener("click", reportarDibujante);
rangoTamano.addEventListener("input", () => tamanoActual = Number(rangoTamano.value));
formChat.addEventListener("submit", enviarMensaje);

document.querySelectorAll(".color").forEach(boton => {
    boton.addEventListener("click", () => {
        document.querySelectorAll(".color").forEach(x => x.classList.remove("activo"));
        boton.classList.add("activo");
        colorActual = boton.dataset.color;
    });
});

pizarra.addEventListener("pointerdown", empezarTrazo);
pizarra.addEventListener("pointermove", moverTrazo);
pizarra.addEventListener("pointerup", terminarTrazo);
pizarra.addEventListener("pointerleave", terminarTrazo);

iniciarConexion();
prepararPizarra();

async function iniciarConexion() {
    conexion = new signalR.HubConnectionBuilder()
        .withUrl("/juegoHub")
        .configureLogging(signalR.LogLevel.Information)
        .withAutomaticReconnect()
        .build();

    registrarEventosServidor();

    try {
        await conexion.start();
        mensajeEntrada.textContent = "";
    } catch (error) {
        mensajeEntrada.textContent = "No se pudo conectar con el servidor.";
        console.log(error);
        setTimeout(iniciarConexion, 5000);
    }
}

function registrarEventosServidor() {
    conexion.on("SalaActualizada", mostrarSalaEspera);
    conexion.on("EntradaRechazada", mostrarEntradaRechazada);
    conexion.on("EntradaEnEspera", mostrarEntradaEnEspera);
    conexion.on("PartidaIniciada", iniciarPartida);
    conexion.on("RondaActualizada", actualizarRonda);
    conexion.on("TrazoRecibido", dibujarTrazo);
    conexion.on("PizarraLimpiada", borrarCanvas);
    conexion.on("MensajeRecibido", agregarMensajeChat);
    conexion.on("PartidaFinalizada", mostrarFinal);
    conexion.on("VolverSalaEspera", mostrarSalaEspera);
}

async function entrarSala() {
    nombreJugador = txtNombre.value.trim();

    if (!nombreJugador) {
        mensajeEntrada.textContent = "Escribe tu nombre para entrar.";
        return;
    }

    btnEntrar.disabled = true;
    mensajeEntrada.textContent = "Conectando...";

    try {
        await conexion.invoke("EntrarSala", nombreJugador);
    } catch (error) {
        mensajeEntrada.textContent = "Error al entrar en la sala.";
        btnEntrar.disabled = false;
        console.log(error);
    }
}

async function marcarListo() {
    btnListo.disabled = true;

    try {
        await conexion.invoke("MarcarListo");
    } catch (error) {
        btnListo.disabled = false;
        console.log(error);
    }
}

async function enviarPalabra() {
    const palabra = txtPalabra.value.trim();

    if (!palabra) return;

    btnEnviarPalabra.disabled = true;
    palabraDibujante = palabra;

    try {
        await conexion.invoke("EnviarPalabra", palabra);
        txtPalabra.value = "";
    } catch (error) {
        btnEnviarPalabra.disabled = false;
        palabraDibujante = "";
        console.log(error);
    }
}

async function enviarMensaje(e) {
    e.preventDefault();

    if (!puedeEnviarMensaje) return;

    const mensaje = txtMensaje.value.trim();
    if (!mensaje) return;

    txtMensaje.value = "";

    try {
        await conexion.invoke("EnviarMensaje", mensaje);
    } catch (error) {
        console.log(error);
    }
}

async function limpiarPizarra() {
    if (!puedeDibujar) return;

    borrarCanvas();

    try {
        await conexion.invoke("LimpiarPizarra");
    } catch (error) {
        console.log(error);
    }
}

async function reportarDibujante() {
    if (btnReportar.disabled) return;

    btnReportar.disabled = true;
    reporteEnviado = true;

    try {
        await conexion.invoke("ReportarDibujante");
    } catch (error) {
        btnReportar.disabled = false;
        reporteEnviado = false;
        console.log(error);
    }
}

function cambiarVista(nombreVista) {
    Object.values(vistas).forEach(vista => vista.classList.add("oculto"));
    vistas[nombreVista].classList.remove("oculto");
}

function mostrarEntradaRechazada(mensaje) {
    btnEntrar.disabled = false;
    mensajeEntrada.textContent = mensaje ?? "Hay una partida en curso. Espera a que termine.";
    cambiarVista("entrada");
}

function mostrarEntradaEnEspera(mensaje) {
    btnEntrar.disabled = true;
    txtNombre.disabled = true;
    mensajeEntrada.textContent = mensaje ?? "Entraras automaticamente cuando termine la partida.";
    cambiarVista("entrada");
}

function mostrarSalaEspera(estado) {
    cambiarVista("espera");
    btnEntrar.disabled = false;
    txtNombre.disabled = false;
    btnListo.disabled = estado?.jugadores?.some(j => j.nombre === nombreJugador && j.listo) ?? false;
    tiempoEspera.textContent = estado?.segundosRestantes ?? 100;
    mensajeEspera.textContent = estado?.mensaje ?? "Cuando todos esten listos empieza la partida.";
    pintarListaEspera(estado?.jugadores ?? []);
}

function pintarListaEspera(jugadores) {
    listaEspera.innerHTML = "";

    jugadores.forEach(jugador => {
        const li = document.createElement("li");
        const estado = jugador.listo ? "Listo" : "Esperando";

        li.innerHTML = `
            <span>${jugador.nombre}</span>
            <span class="${jugador.listo ? "estado-listo" : "estado-espera"}">${estado}</span>
        `;

        listaEspera.appendChild(li);
    });
}

function iniciarPartida(estado) {
    cambiarVista("juego");
    borrarCanvas();
    mensajesChat.innerHTML = "";
    actualizarRonda(estado);
}

function actualizarRonda(estado) {
    const dibujante = estado?.dibujante ?? "";
    const esMiTurno = dibujante === nombreJugador;
    const palabraPendiente = Boolean(estado?.palabraPendiente);

    if (esMiTurno && palabraPendiente) {
        palabraDibujante = "";
    }

    if (palabraPendiente) {
        reporteEnviado = false;
    }

    puedeDibujar = esMiTurno && !palabraPendiente;
    textoTurno.textContent = esMiTurno ? "Tu turno para dibujar" : `Dibuja ${dibujante}`;
    palabraRonda.textContent = esMiTurno && palabraDibujante
        ? palabraDibujante
        : estado?.palabraMostrada ?? "_ _ _ _ _";
    tiempoRonda.textContent = estado?.segundosRestantes ?? 100;
    panelPalabra.classList.toggle("oculto", !esMiTurno || !palabraPendiente);
    herramientas.classList.toggle("oculto", palabraPendiente);
    puedeEnviarMensaje = !esMiTurno && !palabraPendiente;
    txtMensaje.disabled = !puedeEnviarMensaje;
    rangoTamano.disabled = !puedeDibujar;
    btnLimpiar.disabled = !puedeDibujar;
    document.querySelectorAll(".color").forEach(boton => boton.disabled = !puedeDibujar);
    btnReportar.disabled = esMiTurno || palabraPendiente || reporteEnviado;
    btnReportar.title = estado?.reportesNecesarios
        ? `Reportar dibujante (${estado.reportes ?? 0}/${estado.reportesNecesarios})`
        : "Reportar dibujante";
    btnReportar.setAttribute("aria-label", btnReportar.title);
    btnEnviarPalabra.disabled = false;

    pintarJugadores(estado?.jugadores ?? [], dibujante);
}

function pintarJugadores(jugadores, dibujante) {
    listaJugadores.innerHTML = "";

    jugadores.forEach(jugador => {
        const li = document.createElement("li");
        li.classList.toggle("dibujante", jugador.nombre === dibujante);
        li.innerHTML = `
            <span class="jugador-nombre">${jugador.nombre}</span>
            <span class="jugador-puntos">${jugador.puntos ?? 0}</span>
        `;
        listaJugadores.appendChild(li);
    });
}

function agregarMensajeChat(mensaje) {
    const div = document.createElement("div");
    div.className = mensaje.esSistema ? "mensaje-chat mensaje-sistema" : "mensaje-chat";

    if (mensaje.esSistema) {
        div.textContent = mensaje.texto;
    } else {
        div.innerHTML = `<strong>${mensaje.nombre}:</strong> ${mensaje.texto}`;
    }

    mensajesChat.appendChild(div);
    mensajesChat.scrollTop = mensajesChat.scrollHeight;
}

function mostrarFinal(estado) {
    cambiarVista("final");
    textoGanador.textContent = `Gano ${estado?.ganador ?? "un jugador"}`;
    tiempoFinal.textContent = estado?.segundosRestantes ?? 10;
    listaFinal.innerHTML = "";

    (estado?.jugadores ?? []).forEach(jugador => {
        const li = document.createElement("li");
        li.innerHTML = `<span>${jugador.nombre}</span><strong>${jugador.puntos ?? 0} pts</strong>`;
        listaFinal.appendChild(li);
    });
}

function prepararPizarra() {
    contexto.lineCap = "round";
    contexto.lineJoin = "round";
    borrarCanvas();
}

function borrarCanvas() {
    contexto.fillStyle = "#ffffff";
    contexto.fillRect(0, 0, pizarra.width, pizarra.height);
}

function obtenerPunto(evento) {
    const rect = pizarra.getBoundingClientRect();
    return {
        x: (evento.clientX - rect.left) * (pizarra.width / rect.width),
        y: (evento.clientY - rect.top) * (pizarra.height / rect.height)
    };
}

function empezarTrazo(evento) {
    if (!puedeDibujar) return;

    estaDibujando = true;
    ultimoPunto = obtenerPunto(evento);
}

async function moverTrazo(evento) {
    if (!estaDibujando || !puedeDibujar) return;

    const punto = obtenerPunto(evento);
    const trazo = {
        x1: ultimoPunto.x,
        y1: ultimoPunto.y,
        x2: punto.x,
        y2: punto.y,
        color: colorActual,
        size: tamanoActual
    };

    dibujarTrazo(trazo);
    ultimoPunto = punto;

    try {
        await conexion.invoke("EnviarTrazo", trazo);
    } catch (error) {
        console.log(error);
    }
}

function terminarTrazo() {
    estaDibujando = false;
    ultimoPunto = null;
}

function dibujarTrazo(trazo) {
    contexto.strokeStyle = trazo.color;
    contexto.lineWidth = trazo.size;
    contexto.beginPath();
    contexto.moveTo(trazo.x1, trazo.y1);
    contexto.lineTo(trazo.x2, trazo.y2);
    contexto.stroke();
}
