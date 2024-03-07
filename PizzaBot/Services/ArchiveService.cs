using PizzaBot.Models;
using System.Security.Cryptography;

namespace PizzaBot.Services
{
    using PizzaArchiveType = List<PizzaArchiveEntry>;

    public class PizzaArchiveEntry
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

        public event EventHandler OnArchiveChange;

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
            return _pizzaArchive;
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

            if (entry.id == 0)
            {
                entry.id = _rng.Next();
            }
            _pizzaArchive.Add(entry);
            SortArchive();
            _jsonService.WriteNewPizzaArchive(_pizzaArchive);

            OnArchiveChange.Invoke(this, null);

            return true;
        }

        public void ChangeEntry(PizzaArchiveEntry entry)
        {
            lock (this)
            {
                RemoveEntry(entry.id);
                AddEntry(entry);
            }
        }

        public PizzaArchiveEntry GetEntryByID(int id)
        {
            return _pizzaArchive.Find(x => x.id == id);
        }

        public bool RemoveEntry(PizzaArchiveEntry entry)
        {
            bool succesfull = _pizzaArchive.Remove(entry);
            return succesfull;
        }

        public bool RemoveEntry(int id)
        {
            int index = _pizzaArchive.FindIndex((PizzaArchiveEntry o) => { return o.id == id; });
            if (index == -1)
            {
                return false;
            }
            _pizzaArchive.RemoveAt(index);

            OnArchiveChange.Invoke(this, null);

            return true;
        }

        private void SortArchive()
        {
            _pizzaArchive.OrderByDescending(o => o.date);
        }
    }
}
