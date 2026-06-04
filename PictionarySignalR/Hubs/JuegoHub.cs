using Microsoft.AspNetCore.SignalR;
using PictionarySignalR.Models;
using PictionarySignalR.Services;

namespace PictionarySignalR.Hubs
{
    public class JuegoHub : Hub
    {
        private readonly string GrupoSala = "sala";
        private readonly JuegoService juegoService;

        public JuegoHub(JuegoService juegoService)
        {
            this.juegoService = juegoService;
        }

        public async Task EntrarSala(string idJugador, string nombre)
        {
            var resultado = juegoService.EntrarSala(Context.ConnectionId, idJugador, nombre);

            if (resultado.EnEspera)
            {
                await Clients.Caller.SendAsync("EntradaEnEspera", resultado.Mensaje);
                return;
            }

            if (!resultado.Aceptado)
            {
                await Clients.Caller.SendAsync("EntradaRechazada", resultado.Mensaje);
                return;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, GrupoSala);

            if (resultado.Estado == EstadoPartida.EnPartida)
            {
                await Clients.Caller.SendAsync("PartidaIniciada", juegoService.ObtenerEstadoRonda());
                await Clients.Caller.SendAsync("HistorialChat", juegoService.ObtenerHistorialChat());
                await Clients.Caller.SendAsync("HistorialPizarra", juegoService.ObtenerHistorialPizarra());
                await Clients.Group(GrupoSala).SendAsync("RondaActualizada", juegoService.ObtenerEstadoRonda());
            }
            else if (resultado.Estado == EstadoPartida.Finalizada)
            {
                await Clients.Caller.SendAsync("PartidaFinalizada", juegoService.ObtenerResultadoFinal());
            }
            else
            {
                await Clients.Group(GrupoSala).SendAsync("SalaActualizada", juegoService.ObtenerEstadoSala());
            }
        }

        public async Task MarcarListo()
        {
            await juegoService.MarcarListoAsync(Context.ConnectionId);
        }

        public async Task EnviarPalabra(string palabra)
        {
            await juegoService.DefinirPalabraAsync(Context.ConnectionId, palabra);
        }

        public async Task EnviarMensaje(string mensaje)
        {
            await juegoService.ProcesarMensajeAsync(Context.ConnectionId, mensaje);
        }

        public async Task EnviarTrazo(Trazo trazo)
        {
            if (juegoService.PuedeDibujar(Context.ConnectionId))
            {
                juegoService.RegistrarTrazo(trazo);
                await Clients.OthersInGroup(GrupoSala).SendAsync("TrazoRecibido", trazo);
            }
        }

        public async Task LimpiarPizarra()
        {
            if (juegoService.PuedeDibujar(Context.ConnectionId))
            {
                juegoService.LimpiarTrazosPizarra();
                await Clients.OthersInGroup(GrupoSala).SendAsync("PizarraLimpiada");
            }
        }

        public async Task ReportarDibujante()
        {
            await juegoService.ReportarDibujanteAsync(Context.ConnectionId);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await juegoService.SalirSalaAsync(Context.ConnectionId);
            await base.OnDisconnectedAsync(exception);
        }
    }
}
