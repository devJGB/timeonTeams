// Geolocalocalización
window.getCurrentLocation = (dotNetHelper) => {
    if (navigator.geolocation) {
        navigator.geolocation.getCurrentPosition(
            (position) => {
                dotNetHelper.invokeMethodAsync("setLocation", position.coords.latitude, position.coords.longitude);
            },
            (error) => {
                console.error("Error obteniendo la ubicación", error);
                alert("No se pudo obtener la ubicación. Asegúrese de aceptar los permisos.");
            }
        );
    } else {
        alert("Tu navegador no soporta geolocalización.");
    }
};