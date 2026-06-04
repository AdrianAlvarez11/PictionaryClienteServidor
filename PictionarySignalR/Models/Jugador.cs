namespace PictionarySignalR.Models
{
    public class Jugador
    {
        public string? IdJugador { get; set; }
        public string? ConnectionId { get; set; }
        public string? Nombre { get; set; }
        public int Puntos { get; set; } = 0;
        public bool Listo { get; set; } = false;
        public bool Conectado { get; set; } = true;
        public bool EsDibujante { get; set; } = false;
        public bool YaDibujo { get; set; } = false;
        public bool YaAdivino { get; set; } = false;
    }
}
