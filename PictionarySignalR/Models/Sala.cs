namespace PictionarySignalR.Models
{
    public class Sala
    {
        public List<Jugador> Jugadores { get; set; } = [];
        public List<Jugador> JugadoresEspera { get; set; } = [];
        public List<string> ReportesDibujante { get; set; } = [];
        public EstadoPartida Estado { get; set; } = EstadoPartida.Esperando;
        public string? Palabra { get; set; }
        public bool PalabraPendiente { get; set; } = false;
        public int SegundosEspera { get; set; } = 100;
        public int SegundosRonda { get; set; } = 100;
        public int SegundosFinal { get; set; } = 15;
        public int IndiceDibujante { get; set; } = 0;
    }
}
