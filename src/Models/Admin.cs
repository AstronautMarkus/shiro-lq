using System.ComponentModel.DataAnnotations;

namespace shiro_lq.Models;

public class Admin
{
    public int Id { get; set; }

    [Required(ErrorMessage = "El nombre es obligatorio."), StringLength(100, ErrorMessage = "Máximo 100 caracteres.")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "El usuario es obligatorio."), StringLength(50, ErrorMessage = "Máximo 50 caracteres.")]
    [RegularExpression("^[a-zA-Z0-9_.-]+$", ErrorMessage = "Solo letras, números, punto, guion y guion bajo.")]
    public string Username { get; set; } = "";

    [Required(ErrorMessage = "El email es obligatorio."), EmailAddress(ErrorMessage = "Email no válido."), StringLength(255, ErrorMessage = "Máximo 255 caracteres.")]
    public string Email { get; set; } = "";

    // Form input only, never stored: AdminStore saves PasswordHash. Empty on edit keeps the current password.
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Entre 8 y 100 caracteres.")]
    public string? Password { get; set; }

    public string PasswordHash { get; set; } = "";
}
