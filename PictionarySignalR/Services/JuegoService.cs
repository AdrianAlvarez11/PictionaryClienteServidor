using Microsoft.AspNetCore.SignalR;
using PictionarySignalR.Hubs;
using PictionarySignalR.Models;

namespace PictionarySignalR.Services
{
    public class JuegoService
    {
        private readonly string GrupoSala = "sala";
        private readonly int DuracionEspera = 100;
        private readonly int DuracionRonda = 100;
        private readonly int DuracionFinal = 10;

        private readonly IHubContext<JuegoHub> hubContext;
        private readonly Sala sala = new();

        private Timer? timerEspera;
        private Timer? timerRonda;
        private Timer? timerFinal;

        private bool cerrandoRonda = false;

        public JuegoService(IHubContext<JuegoHub> hubContext)
        {
            this.hubContext = hubContext;
        }

        public ResultadoEntrada EntrarSala(string idJugador, string nombre)
        {
            lock (sala.Jugadores)
            {
                nombre = nombre.Trim();

                if (sala.Estado != EstadoPartida.Esperando)
                {
                    if (sala.JugadoresEspera.Any(x => x.IdJugador == idJugador))
                    {
                        return new ResultadoEntrada
                        {
                            Aceptado = false,
                            EnEspera = true,
                            Mensaje = "Sigues en espera. Entraras automaticamente cuando termine la partida."
                        };
                    }

                    if (NombreEnUso(nombre))
                    {
                        return new ResultadoEntrada
                        {
                            Aceptado = false,
                            Mensaje = "Ese nombre ya esta siendo usado."
                        };
                    }

                    if (!sala.JugadoresEspera.Any(x => x.IdJugador == idJugador))
                    {
                        sala.JugadoresEspera.Add(new Jugador
                        {
                            IdJugador = idJugador,
                            Nombre = nombre
                        });
                    }

                    return new ResultadoEntrada
                    {
                        Aceptado = false,
                        EnEspera = true,
                        Mensaje = "Hay una partida en curso. Entraras automaticamente cuando termine."
                    };
                }

                if (sala.Jugadores.Any(x => x.IdJugador == idJugador))
                {
                    return new ResultadoEntrada { Aceptado = true };
                }

                if (NombreEnUso(nombre))
                {
                    return new ResultadoEntrada
                    {
                        Aceptado = false,
                        Mensaje = "Ese nombre ya esta siendo usado."
                    };
                }

                sala.Jugadores.Add(new Jugador
                {
                    IdJugador = idJugador,
                    Nombre = nombre
                });

                IniciarTimerEspera();

                return new ResultadoEntrada { Aceptado = true };
            }
        }

        public async Task SalirSalaAsync(string idJugador)
        {
            //manejar todos los casos de cuando sale un jugador. 
            bool debeVolverAEspera = false;
            bool debeEnviarSala = false;
            bool debeEnviarRonda = false;
            bool debeLimpiarPizarra = false;
            bool debeTerminarRonda = false;
            bool debeFinalizarPartida = false;
            List<string> jugadoresParaAgregarGrupo = [];
            MensajeChat? mensaje = null;

            lock (sala.Jugadores)
            {
                var jugador = BuscarJugador(idJugador);
                if (jugador == null)
                {
                    var jugadorEnEspera = sala.JugadoresEspera.FirstOrDefault(x => x.IdJugador == idJugador);

                    if (jugadorEnEspera != null)
                    {
                        sala.JugadoresEspera.Remove(jugadorEnEspera);
                    }

                    return;
                }

                var estabaEnPartida = sala.Estado == EstadoPartida.EnPartida;
                var eraDibujante = jugador.EsDibujante;
                var indiceJugador = sala.Jugadores.IndexOf(jugador);

                // Se ajusta el indice porque la rotacion de turnos depende de la posicion en la lista.
                sala.Jugadores.Remove(jugador);
                AjustarIndiceDibujante(indiceJugador);

                sala.ReportesDibujante.Remove(idJugador);

                mensaje = new MensajeChat
                {
                    EsSistema = true,
                    Texto = $"{jugador.Nombre} salio de la sala."
                };
                if (sala.Jugadores.Count < 2 && sala.Estado == EstadoPartida.EnPartida)
                {
                    debeVolverAEspera = true;
                    ReiniciarSalaParaEspera();
                    jugadoresParaAgregarGrupo = PasarJugadoresEsperaASala();
                }
                else if (sala.Estado == EstadoPartida.Esperando && sala.Jugadores.Count < 2)
                {
                    DetenerTimerEspera();
                    debeEnviarSala = true;
                }
                else if (sala.Estado == EstadoPartida.Esperando)
                {
                    debeEnviarSala = true;
                }
                else if (estabaEnPartida && eraDibujante)
                {
                    DetenerTimerRonda();

                    if (sala.Jugadores.All(x => x.YaDibujo))
                    {
                        debeFinalizarPartida = true;
                    }
                    else
                    {
                        PrepararRonda();
                        debeEnviarRonda = true;
                        debeLimpiarPizarra = true;
                    }
                }
                else if (estabaEnPartida &&
                    !sala.PalabraPendiente &&
                    sala.Jugadores.Where(x => !x.EsDibujante).All(x => x.YaAdivino))
                {
                    debeTerminarRonda = true;
                }
                else if (estabaEnPartida)
                {
                    debeEnviarRonda = true;
                }
            }

            if (mensaje != null)
            {
                await hubContext.Clients.Group(GrupoSala).SendAsync("MensajeRecibido", mensaje);
            }

            if (debeVolverAEspera)
            {
                await AgregarJugadoresAlGrupoAsync(jugadoresParaAgregarGrupo);
                await hubContext.Clients.Group(GrupoSala).SendAsync("VolverSalaEspera", ObtenerEstadoSala());
            }
            else if (debeFinalizarPartida)
            {
                await FinalizarPartidaAsync();
            }
            else if (debeTerminarRonda)
            {
                await TerminarRondaAsync();
            }
            else
            {
                if (debeLimpiarPizarra)
                {
                    await hubContext.Clients.Group(GrupoSala).SendAsync("PizarraLimpiada");
                }

                if (debeEnviarSala)
                {
                    await EnviarEstadoSalaAsync();
                }

                if (debeEnviarRonda)
                {
                    await EnviarEstadoRondaAsync();
                }
            }
        }

        public async Task MarcarListoAsync(string idJugador)
        {
            bool iniciarPartida;

            lock (sala.Jugadores)
            {
                var jugador = BuscarJugador(idJugador);

                if (jugador == null || sala.Estado != EstadoPartida.Esperando)
                {
                    return;
                }

                jugador.Listo = true;
                iniciarPartida = sala.Jugadores.Count >= 2 && sala.Jugadores.All(x => x.Listo);
            }

            if (iniciarPartida)
            {
                await IniciarPartidaAsync();
            }
            else
            {
                await EnviarEstadoSalaAsync();
            }
        }

        public bool PuedeDibujar(string idJugador)
        {
            lock (sala.Jugadores)
            {
                var jugador = BuscarJugador(idJugador);
                return jugador?.EsDibujante == true &&
                       sala.Estado == EstadoPartida.EnPartida &&
                       sala.PalabraPendiente == false;
            }
        }

        public async Task DefinirPalabraAsync(string idJugador, string palabra)
        {
            MensajeChat? mensaje = null;
            bool palabraAceptada = false;

            lock (sala.Jugadores)
            {
                var jugador = BuscarJugador(idJugador);

                if (jugador?.EsDibujante == true &&
                    sala.Estado == EstadoPartida.EnPartida &&
                    sala.PalabraPendiente &&
                    !string.IsNullOrWhiteSpace(palabra))
                {
                    sala.Palabra = palabra.Trim();
                    sala.PalabraPendiente = false;
                    sala.SegundosRonda = DuracionRonda;
                    palabraAceptada = true;
                    IniciarTimerRonda();

                    mensaje = new MensajeChat
                    {
                        EsSistema = true,
                        Texto = $"{jugador.Nombre} ya eligio palabra. Empieza la ronda."
                    };
                }
            }

            if (palabraAceptada)
            {
                await hubContext.Clients.Group(GrupoSala).SendAsync("MensajeRecibido", mensaje);
                await EnviarEstadoRondaAsync();
            }
        }

        public async Task ProcesarMensajeAsync(string idJugador, string texto)
        {
            MensajeChat? mensaje = null;
            bool actualizarRonda = false;
            bool terminarRonda = false;

            lock (sala.Jugadores)
            {
                var jugador = BuscarJugador(idJugador);

                if (jugador == null ||
                    sala.Estado != EstadoPartida.EnPartida ||
                    string.IsNullOrWhiteSpace(texto))
                {
                    return;
                }

                texto = texto.Trim();

                // Cada mensaje de un jugador que adivina tambien funciona como intento de respuesta.
                if (jugador.EsDibujante)
                {
                    mensaje = new MensajeChat
                    {
                        EsSistema = true,
                        Texto = "El dibujante no puede adivinar en su propio turno."
                    };
                }
                else if (sala.PalabraPendiente || string.IsNullOrWhiteSpace(sala.Palabra))
                {
                    return;
                }
                else if (jugador.YaAdivino)
                {
                    mensaje = new MensajeChat
                    {
                        EsSistema = true,
                        Texto = $"{jugador.Nombre} ya habia acertado esta ronda."
                    };
                }
                else if (texto.ToUpper() == sala.Palabra.ToUpper())
                {
                    jugador.YaAdivino = true;
                    jugador.Puntos += sala.SegundosRonda;

                    mensaje = new MensajeChat
                    {
                        EsSistema = true,
                        Texto = $"{jugador.Nombre} acerto y gano {sala.SegundosRonda} puntos."
                    };

                    actualizarRonda = true;
                    terminarRonda = sala.Jugadores
                        .Where(x => !x.EsDibujante)
                        .All(x => x.YaAdivino);
                }
                else
                {
                    mensaje = new MensajeChat
                    {
                        Nombre = jugador.Nombre,
                        Texto = texto
                    };
                }

            }

            await hubContext.Clients.Group(GrupoSala).SendAsync("MensajeRecibido", mensaje);

            if (actualizarRonda)
            {
                await EnviarEstadoRondaAsync();
            }

            if (terminarRonda)
            {
                await TerminarRondaAsync();
            }
        }

        public async Task ReportarDibujanteAsync(string idJugador)
        {
            MensajeChat? mensaje = null;
            bool expulsarDibujante = false;
            bool volverSala = false;
            bool finalizarPartida = false;
            bool enviarRonda = false;
            List<string> jugadoresParaAgregarGrupo = [];

            lock (sala.Jugadores)
            {
                var jugador = BuscarJugador(idJugador);
                var dibujante = sala.Jugadores.FirstOrDefault(x => x.EsDibujante);

                if (jugador == null ||
                    dibujante == null ||
                    jugador.EsDibujante ||
                    sala.Estado != EstadoPartida.EnPartida ||
                    sala.PalabraPendiente)
                {
                    return;
                }

                if (sala.ReportesDibujante.Contains(idJugador))
                {
                    mensaje = new MensajeChat
                    {
                        EsSistema = true,
                        Texto = $"{jugador.Nombre} ya habia reportado esta ronda."
                    };
                    enviarRonda = true;
                }
                else
                {
                    sala.ReportesDibujante.Add(idJugador);

                    var necesarios = CalcularReportesNecesarios();
                    var reportes = sala.ReportesDibujante.Count;
                    // La mayoria de jugadores que adivinan puede expulsar al dibujante.
                    expulsarDibujante = reportes >= necesarios;

                    if (expulsarDibujante)
                    {
                        DetenerTimerRonda();

                        mensaje = new MensajeChat
                        {
                            EsSistema = true,
                            Texto = $"{dibujante.Nombre} fue expulsado por reportes."
                        };
                        var indiceDibujante = sala.Jugadores.IndexOf(dibujante);
                        sala.Jugadores.Remove(dibujante);
                        AjustarIndiceDibujante(indiceDibujante);

                        if (sala.Jugadores.Count < 2)
                        {
                            volverSala = true;
                            ReiniciarSalaParaEspera();
                            jugadoresParaAgregarGrupo = PasarJugadoresEsperaASala();
                        }
                        else if (sala.Jugadores.All(x => x.YaDibujo))
                        {
                            finalizarPartida = true;
                        }
                        else
                        {
                            PrepararRonda();
                            enviarRonda = true;
                        }
                    }
                    else
                    {
                        mensaje = new MensajeChat
                        {
                            EsSistema = true,
                            Texto = $"{jugador.Nombre} reporto al dibujante. Reportes: {reportes}/{necesarios}."
                        };
                        enviarRonda = true;
                    }
                }
            }

            if (mensaje != null)
            {
                await hubContext.Clients.Group(GrupoSala).SendAsync("MensajeRecibido", mensaje);
            }

            if (volverSala)
            {
                await AgregarJugadoresAlGrupoAsync(jugadoresParaAgregarGrupo);
                await hubContext.Clients.Group(GrupoSala).SendAsync("VolverSalaEspera", ObtenerEstadoSala());
            }
            else if (finalizarPartida)
            {
                await FinalizarPartidaAsync();
            }
            else if (expulsarDibujante)
            {
                await hubContext.Clients.Group(GrupoSala).SendAsync("PizarraLimpiada");
                await EnviarEstadoRondaAsync();
            }
            else if (enviarRonda)
            {
                await EnviarEstadoRondaAsync();
            }
        }

        public EstadoSalaDto ObtenerEstadoSala()
        {
            lock (sala.Jugadores)
            {
                return CrearEstadoSala();
            }
        }

        public EstadoRondaDto ObtenerEstadoRonda()
        {
            lock (sala.Jugadores)
            {
                return CrearEstadoRonda();
            }
        }

        private async Task IniciarPartidaAsync()
        {
            lock (sala.Jugadores)
            {
                DetenerTimerEspera();

                // Al empezar una partida nueva se reinician puntajes y banderas de rondas anteriores.
                sala.Estado = EstadoPartida.EnPartida;
                sala.IndiceDibujante = 0;
                cerrandoRonda = false;

                foreach (var jugador in sala.Jugadores)
                {
                    jugador.Puntos = 0;
                    jugador.Listo = false;
                    jugador.YaDibujo = false;
                    jugador.YaAdivino = false;
                    jugador.EsDibujante = false;
                }

                PrepararRonda();
            }

            await hubContext.Clients.Group(GrupoSala).SendAsync("PizarraLimpiada");
            await hubContext.Clients.Group(GrupoSala).SendAsync("PartidaIniciada", ObtenerEstadoRonda());
        }

        private async Task TerminarRondaAsync()
        {
            bool finalizarPartida;
            MensajeChat mensaje;

            lock (sala.Jugadores)
            {
                if (cerrandoRonda || sala.Estado != EstadoPartida.EnPartida)
                {
                    return;
                }

                cerrandoRonda = true;
                DetenerTimerRonda();

                var dibujante = sala.Jugadores.FirstOrDefault(x => x.EsDibujante);
                var alguienAdivino = sala.Jugadores.Any(x => !x.EsDibujante && x.YaAdivino);

                // Si nadie acerto, el castigo es para el dibujante de la ronda.
                if (!alguienAdivino && dibujante != null)
                {
                    dibujante.Puntos -= 100;
                    mensaje = new MensajeChat
                    {
                        EsSistema = true,
                        Texto = $"Nadie adivino. {dibujante.Nombre} pierde 100 puntos. La palabra era: {sala.Palabra}."
                    };
                }
                else
                {
                    mensaje = new MensajeChat
                    {
                        EsSistema = true,
                        Texto = $"Termino la ronda. La palabra era: {sala.Palabra}."
                    };
                }

                finalizarPartida = sala.Jugadores.All(x => x.YaDibujo);
            }

            await hubContext.Clients.Group(GrupoSala).SendAsync("MensajeRecibido", mensaje);
            await EnviarEstadoRondaAsync();

            await Task.Delay(3000);

            if (finalizarPartida)
            {
                await FinalizarPartidaAsync();
                return;
            }

            lock (sala.Jugadores)
            {
                SiguienteDibujante();
                PrepararRonda();
                cerrandoRonda = false;
            }

            await hubContext.Clients.Group(GrupoSala).SendAsync("PizarraLimpiada");
            await EnviarEstadoRondaAsync();
        }

        private async Task FinalizarPartidaAsync()
        {
            ResultadoFinalDto resultado;

            lock (sala.Jugadores)
            {
                sala.Estado = EstadoPartida.Finalizada;
                sala.SegundosFinal = DuracionFinal;
                DetenerTimerRonda();

                timerFinal?.Dispose();
                timerFinal = new Timer(_ => _ = TickFinalAsync(), null, 1000, 1000);
                resultado = CrearResultadoFinal();
            }

            await hubContext.Clients.Group(GrupoSala).SendAsync("PartidaFinalizada", resultado);
        }

        private async Task TickEsperaAsync()
        {
            bool iniciarPartida = false;

            lock (sala.Jugadores)
            {
                // El timer de sala solo corre cuando ya hay suficientes jugadores esperando.
                if (sala.Estado != EstadoPartida.Esperando || sala.Jugadores.Count < 2)
                {
                    DetenerTimerEspera();
                    return;
                }

                sala.SegundosEspera--;
                iniciarPartida = sala.SegundosEspera <= 0;
            }

            if (iniciarPartida)
            {
                await IniciarPartidaAsync();
            }
            else
            {
                await EnviarEstadoSalaAsync();
            }
        }

        private async Task TickRondaAsync()
        {
            bool terminarRonda = false;

            lock (sala.Jugadores)
            {
                // La ronda empieza a contar hasta que el dibujante captura la palabra.
                if (sala.Estado != EstadoPartida.EnPartida || sala.PalabraPendiente)
                {
                    return;
                }

                sala.SegundosRonda--;
                terminarRonda = sala.SegundosRonda <= 0;
            }

            if (terminarRonda)
            {
                await TerminarRondaAsync();
            }
            else
            {
                await EnviarEstadoRondaAsync();
            }
        }

        private async Task TickFinalAsync()
        {
            bool volverSala = false;
            ResultadoFinalDto? resultado = null;
            List<string> jugadoresParaAgregarGrupo = [];

            lock (sala.Jugadores)
            {
                if (sala.Estado != EstadoPartida.Finalizada)
                {
                    timerFinal?.Dispose();
                    timerFinal = null;
                    return;
                }

                sala.SegundosFinal--;
                volverSala = sala.SegundosFinal <= 0;

                if (!volverSala)
                {
                    resultado = CrearResultadoFinal();
                }
            }

            if (volverSala)
            {
                lock (sala.Jugadores)
                {
                    ReiniciarSalaParaEspera();
                    jugadoresParaAgregarGrupo = PasarJugadoresEsperaASala();
                    IniciarTimerEspera();
                }

                await AgregarJugadoresAlGrupoAsync(jugadoresParaAgregarGrupo);
                await hubContext.Clients.Group(GrupoSala).SendAsync("VolverSalaEspera", ObtenerEstadoSala());
            }
            else
            {
                await hubContext.Clients.Group(GrupoSala).SendAsync("PartidaFinalizada", resultado);
            }
        }

        private void PrepararRonda()
        {
            // Se limpia el estado por jugador y se marca al siguiente dibujante.
            foreach (var jugador in sala.Jugadores)
            {
                jugador.EsDibujante = false;
                jugador.YaAdivino = false;
            }

            var dibujante = sala.Jugadores[sala.IndiceDibujante];
            dibujante.EsDibujante = true;
            dibujante.YaDibujo = true;

            sala.Palabra = null;
            sala.PalabraPendiente = true;
            sala.ReportesDibujante.Clear();
            sala.SegundosRonda = DuracionRonda;
        }

        private void SiguienteDibujante()
        {
            if (sala.Jugadores.Count == 0)
            {
                sala.IndiceDibujante = 0;
                return;
            }

            sala.IndiceDibujante++;

            if (sala.IndiceDibujante >= sala.Jugadores.Count)
            {
                sala.IndiceDibujante = 0;
            }
        }

        private EstadoSalaDto CrearEstadoSala()
        {
            return new EstadoSalaDto
            {
                Jugadores = CrearJugadoresDto(),
                SegundosRestantes = sala.SegundosEspera,
                Mensaje = sala.Jugadores.Count < 2
                    ? "Esperando al menos 2 jugadores..."
                    : "La partida inicia cuando todos esten listos o termine el contador."
            };
        }

        private EstadoRondaDto CrearEstadoRonda()
        {
            var dibujante = sala.Jugadores.FirstOrDefault(x => x.EsDibujante);

            return new EstadoRondaDto
            {
                Jugadores = CrearJugadoresDto(),
                Dibujante = dibujante?.Nombre ?? "",
                PalabraMostrada = sala.PalabraPendiente
                    ? "Esperando palabra..."
                    : OcultarPalabra(sala.Palabra ?? ""),
                PalabraPendiente = sala.PalabraPendiente,
                SegundosRestantes = sala.SegundosRonda,
                Reportes = sala.ReportesDibujante.Count,
                ReportesNecesarios = CalcularReportesNecesarios()
            };
        }

        private ResultadoFinalDto CrearResultadoFinal()
        {
            var ganador = sala.Jugadores
                .OrderByDescending(x => x.Puntos)
                .FirstOrDefault();

            return new ResultadoFinalDto
            {
                Jugadores = CrearJugadoresDto()
                    .OrderByDescending(x => x.Puntos)
                    .ToList(),
                Ganador = ganador?.Nombre ?? "Nadie",
                SegundosRestantes = sala.SegundosFinal
            };
        }

        private List<JugadorDto> CrearJugadoresDto()
        {
            return sala.Jugadores.Select(x => new JugadorDto
            {
                Nombre = x.Nombre ?? "",
                Puntos = x.Puntos,
                Listo = x.Listo
            }).ToList();
        }

        private Jugador? BuscarJugador(string idJugador)
        {
            return sala.Jugadores.FirstOrDefault(x => x.IdJugador == idJugador);
        }

        private bool NombreEnUso(string nombre)
        {
            return sala.Jugadores.Any(x => x.Nombre?.Equals(nombre, StringComparison.OrdinalIgnoreCase) == true) ||
                   sala.JugadoresEspera.Any(x => x.Nombre?.Equals(nombre, StringComparison.OrdinalIgnoreCase) == true);
        }

        private void IniciarTimerEspera()
        {
            if (sala.Jugadores.Count >= 2 && timerEspera == null && sala.Estado == EstadoPartida.Esperando)
            {
                sala.SegundosEspera = DuracionEspera;
                timerEspera = new Timer(_ => _ = TickEsperaAsync(), null, 1000, 1000);
            }
        }

        private void IniciarTimerRonda()
        {
            timerRonda?.Dispose();
            timerRonda = new Timer(_ => _ = TickRondaAsync(), null, 1000, 1000);
        }

        private void DetenerTimerEspera()
        {
            timerEspera?.Dispose();
            timerEspera = null;
            sala.SegundosEspera = DuracionEspera;
        }

        private void DetenerTimerRonda()
        {
            timerRonda?.Dispose();
            timerRonda = null;
        }

        private void ReiniciarSalaParaEspera()
        {
            timerFinal?.Dispose();
            timerFinal = null;
            DetenerTimerRonda();

            sala.Estado = EstadoPartida.Esperando;
            sala.Palabra = null;
            sala.PalabraPendiente = false;
            sala.ReportesDibujante.Clear();
            sala.IndiceDibujante = 0;
            sala.SegundosFinal = DuracionFinal;
            cerrandoRonda = false;

            foreach (var jugador in sala.Jugadores)
            {
                jugador.Listo = false;
                jugador.EsDibujante = false;
                jugador.YaAdivino = false;
                jugador.YaDibujo = false;
            }
        }

        private List<string> PasarJugadoresEsperaASala()
        {
            var ids = sala.JugadoresEspera
                .Where(x => x.IdJugador != null)
                .Select(x => x.IdJugador!)
                .ToList();

            foreach (var jugador in sala.JugadoresEspera)
            {
                jugador.Listo = false;
                jugador.EsDibujante = false;
                jugador.YaAdivino = false;
                jugador.YaDibujo = false;
                jugador.Puntos = 0;
                sala.Jugadores.Add(jugador);
            }

            sala.JugadoresEspera.Clear();
            return ids;
        }

        private async Task AgregarJugadoresAlGrupoAsync(List<string> idsJugadores)
        {
            foreach (var idJugador in idsJugadores)
            {
                await hubContext.Groups.AddToGroupAsync(idJugador, GrupoSala);
            }
        }

        private async Task EnviarEstadoSalaAsync()
        {
            await hubContext.Clients.Group(GrupoSala).SendAsync("SalaActualizada", ObtenerEstadoSala());
        }

        private async Task EnviarEstadoRondaAsync()
        {
            await hubContext.Clients.Group(GrupoSala).SendAsync("RondaActualizada", ObtenerEstadoRonda());
        }

        private int CalcularReportesNecesarios()
        {
            var jugadoresQueAdivinan = sala.Jugadores.Count(x => !x.EsDibujante);

            if (jugadoresQueAdivinan <= 0)
            {
                return 1;
            }

            return (jugadoresQueAdivinan / 2) + 1;
        }

        private void AjustarIndiceDibujante(int indiceEliminado)
        {
            if (indiceEliminado < 0 || sala.Jugadores.Count == 0)
            {
                sala.IndiceDibujante = 0;
                return;
            }

            if (indiceEliminado < sala.IndiceDibujante)
            {
                sala.IndiceDibujante--;
            }

            if (sala.IndiceDibujante >= sala.Jugadores.Count)
            {
                sala.IndiceDibujante = 0;
            }
        }

        private static string OcultarPalabra(string palabra)
        {
            return string.Join(" ", palabra.Select(letra =>
                char.IsLetterOrDigit(letra) ? '_' : letra));
        }

    }
}
