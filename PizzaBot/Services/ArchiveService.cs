using PizzaBot.Models;
using System.Security.Cryptography;

namespace PizzaBot.Services
{
    using PizzaArchiveType = List<PizzaArchiveEntry>;

    public struct PizzaArchiveEntry
    {
        // id should be a random number
        public int id { get; set; }
        public DateTime date { get; set; }
        public int MeatPizzas { get; set; }
        public int VeggiePizzas { get; set; }
        public int VeganPizzas { get; set; }
        public float TotalCost { get; set; }
        public int Bottles { get; set; }
        public List<AnonymizedOrder> AnonymizedOrders { get; set; }
        public PenaltyType PenaltyType { get; set; }
        public string Annotation { get; set; }

        public struct AnonymizedOrder
        {
            public int requestedMeatPieces { get; set; }
            public int requestedVeggiePieces { get; set; }
            public int requestedVeganPieces { get; set; }
        }
    }
    public class ArchiveService
    {
        JSONService _jsonService;
        PizzaArchiveType _pizzaArchive;
        Random _rng;

        public ArchiveService(JSONService jSONService)
        {
            _rng = new Random();

            _jsonService = jSONService;
            PizzaArchiveType? loaded = _jsonService.GetPizzaArchive();
            _pizzaArchive = loaded == null ? new PizzaArchiveType() : loaded;
            SortArchive();
        }

        public PizzaArchiveType GetLatestNEntries(int n, int startPoint = 0)
        {
            return _pizzaArchive.Skip(startPoint).Take(n).ToList();
        }

        public PizzaArchiveType GetAllEntries()
        {
            PizzaArchiveType copy = _pizzaArchive;
            PizzaArchiveType? loaded = _jsonService.GetPizzaArchive();
            _pizzaArchive = loaded == null ? new PizzaArchiveType() : loaded;
            return copy;
        }

        /// <summary>
        /// Adds an entry to the archive
        /// </summary>
        /// <param name="entry">the data to add</param>
        /// <returns>true, if sucessfull, false otherwise</returns>
        public bool AddEntry(PizzaArchiveEntry entry)
        {
            if (_pizzaArchive == null)
            {
                return false;
            }

            entry.id = _rng.Next();
            _pizzaArchive.Add(entry);
            SortArchive();
            _jsonService.WriteNewPizzaArchive(_pizzaArchive);
            return true;
        }

        public bool RemoveEntry(PizzaArchiveEntry entry)
        {
            return _pizzaArchive.Remove(entry);
        }

        public bool RemoveEntry(int id)
        {
            int index = _pizzaArchive.FindIndex((PizzaArchiveEntry o) => { return o.id == id; });
            if (index == -1)
            {
                return false;
            }
            _pizzaArchive.RemoveAt(index);
            return true;
        }

        private void SortArchive()
        {
            _pizzaArchive.OrderByDescending(o => o.date);
        }
    }
}
