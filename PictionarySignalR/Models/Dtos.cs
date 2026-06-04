namespace PictionarySignalR.Models
{
    public class JugadorDto
    {
        public string Nombre { get; set; } = "";
        public int Puntos { get; set; }
        public bool Listo { get; set; }
        public bool Conectado { get; set; }
    }

    public class EstadoSalaDto
    {
        public List<JugadorDto> Jugadores { get; set; } = [];
        public int SegundosRestantes { get; set; }
        public string Mensaje { get; set; } = "";
    }

    public class EstadoRondaDto
    {
        public List<JugadorDto> Jugadores { get; set; } = [];
        public string Dibujante { get; set; } = "";
        public string PalabraMostrada { get; set; } = "";
        public bool PalabraPendiente { get; set; }
        public int SegundosRestantes { get; set; }
        public int Reportes { get; set; }
        public int ReportesNecesarios { get; set; }
    }

    public class ResultadoFinalDto
    {
        public List<JugadorDto> Jugadores { get; set; } = [];
        public string Ganador { get; set; } = "";
        public int SegundosRestantes { get; set; }
    }

    public class ResultadoEntrada
    {
        public bool Aceptado { get; set; }
        public bool EnEspera { get; set; }
        public EstadoPartida Estado { get; set; } = EstadoPartida.Esperando;
        public string Mensaje { get; set; } = "";
    }
}
