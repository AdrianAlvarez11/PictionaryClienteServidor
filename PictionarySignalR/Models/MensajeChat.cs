namespace PictionarySignalR.Models
{
    public class MensajeChat
    {
        public string? Nombre { get; set; }
        public string Texto { get; set; } = "";
        public bool EsSistema { get; set; } = false;
    }
}
