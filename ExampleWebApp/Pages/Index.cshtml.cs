using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ProjectGeoShot.Web.Pages
{
    public class IndexModel : PageModel
    {
        [BindProperty(SupportsGet = true)]
        public double Lat { get; set; } = 41.9464;

        [BindProperty(SupportsGet = true)]
        public double Lon { get; set; } = -87.7159;

        public void OnGet()
        {
        }
    }
}
