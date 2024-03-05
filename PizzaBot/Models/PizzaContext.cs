using Microsoft.EntityFrameworkCore;

namespace PizzaBot.Models
{
    public class PizzaContext : DbContext
    {
        public DbSet<PizzaRequest> Requests { get; set; }
        public DbSet<PizzaResult> Results { get; set; }

        public PizzaContext(DbContextOptions options) : base(options) { }


    }
}
