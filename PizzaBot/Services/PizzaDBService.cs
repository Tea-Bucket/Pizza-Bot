using PizzaBot.Models;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics.CodeAnalysis;

namespace PizzaBot.Services
{
    class PizzaRequestNameEqualityComparer : IEqualityComparer<PizzaRequest>
    {
        public bool Equals(PizzaRequest? x, PizzaRequest? y)
        {
            if (ReferenceEquals(x, y)) return true;

            if (x == null || y == null) return false;

            if (x.Name == y.Name) return true;

            return false;
        }

        public int GetHashCode([DisallowNull] PizzaRequest obj)
        {
            return obj.Name.GetHashCode();
        }
    }

    public class PizzaDBService
    {
        private readonly PizzaContext _context;
        private readonly PizzaBalancingService _balancingService;
        private readonly GlobalStuffService _globalStuffService;

        private PizzaRequestNameEqualityComparer _reqNameEqualityComparer = new PizzaRequestNameEqualityComparer();
        private Random _rnd = new Random();

        public PizzaDBService(PizzaContext context, PizzaBalancingService balancingService, GlobalStuffService globalStuffService)
        {
            _context = context;
            _balancingService = balancingService;
            _globalStuffService = globalStuffService;
        }

        public PizzaRequest? Create(PizzaRequest request, out string ErrorMessage)
        {
            ErrorMessage = "";
            request.Name = request.Name.Trim();
            //test if orders are closed
            if (_globalStuffService.OrdersLocked)
            {
                ErrorMessage = "Orders are locked, you are too late.";
                return null;
            }
            //test if request is valid
            if (request == null)
            {
                ErrorMessage = "Request was null. If you see this, contact the admin!";
                return null;
            }
            if (_context.Requests.AsEnumerable().Contains(request, _reqNameEqualityComparer))
            {
                ErrorMessage = $"Request with name {request.Name} already exists. Use a different name!";
                return null;
            }
            if (request.Name == null || request.Name == "")
            {
                ErrorMessage = "Request needs a name!";
                return null;
            }
            if (request.Name.Length > _globalStuffService.GetConfig().NameLength)
            {
                ErrorMessage = $"Request name is too long! Max Length: {_globalStuffService.GetConfig().NameLength}";
                return null;
            }
            if (request.reqPiecesVegan + request.reqPiecesVegetarian + request.reqPiecesMeat < 1)
            {
                ErrorMessage = "Request needs to have at least one piece!";
                return null;
            }

            //insert valid request
            request.Id = _rnd.Next(int.MaxValue);
            _context.Requests.Add(request);
            _context.SaveChanges();

            _globalStuffService.ShouldBalance = true;

            return request;
        }

        public IEnumerable<PizzaRequest> GetAllRequests()
        {
            if (_globalStuffService.ShouldBalance)
            {
                Balance();
            }

            return _context.Requests.OrderBy(r => r.Name);
        }

        public IEnumerable<PizzaResult> GetAllResults()
        {
            if (_globalStuffService.ShouldBalance)
            {
                Balance();
            }

            return _context.Results.ToList();
        }

        public PizzaResult? GetResultById(int id)
        {
            if (_globalStuffService.ShouldBalance)
            {
                Balance();
            }

            return _context.Results.Find(id);
        }

        public PizzaRequest? GetRequestById(int id)
        {
            if(_context.Requests.Find(id) == null)
            {
                return null;
            }

            return _context.Requests.Find(id).GetShallowCopy();
        }

        public void DeleteById(int id)
        {
            var request = _context.Requests.Find(id);
            var result = _context.Results.Find(id);

            if (request != null)
            {
                _context.Requests.Remove(request);
            }
            if (result != null)
            {
                _context.Results.Remove(result);
            }

            _globalStuffService.ShouldBalance = true;

            _context.SaveChanges();
        }

        public bool UpdateRequest(PizzaRequest request, out string ErrorMessage)
        {
            ErrorMessage = "";
            //test if orders are closed
            if (_globalStuffService.OrdersLocked)
            {
                ErrorMessage = "Orders are locked, you are too late.";
                return false;
            }
            //test if request is valid
            if (request == null)
            {
                ErrorMessage = "Request was null. If you see this, contact the admin!";
                return false;
            }
            if (request.reqPiecesVegan + request.reqPiecesVegetarian + request.reqPiecesMeat < 1)
            {
                ErrorMessage = "Request needs to have at least one piece!";
                return false;
            }
            _context.Requests.Remove(_context.Requests.Find(request.Id));
            _context.Requests.Add(request);
            _context.SaveChanges();

            _globalStuffService.ShouldBalance = true;

            return true;
        }

        public void Balance()
        {
            Dictionary<int, PizzaRequest> orders = new Dictionary<int, PizzaRequest>();

            foreach (var request in _context.Requests.ToList())
            {
                orders.Add(request.Id, request);
            }

            var balancingResult = _balancingService.Distribute(orders);

            _context.Results.RemoveRange(_context.Results.ToList());
            _context.Results.AddRange(balancingResult.results.Values);

            _globalStuffService.MeatPizzas = balancingResult.requiredMeat;
            _globalStuffService.VeggiePizzas = balancingResult.requiredVeggie;
            _globalStuffService.VeganPizzas = balancingResult.requiredVegan;

            _globalStuffService.TotalCost = balancingResult.totalCost;
            _context.SaveChanges();

            _globalStuffService.ShouldBalance = false;
        }

        public bool DeleteAllOrders(string passPhrase)
        {
            if (passPhrase == Environment.GetEnvironmentVariable("DELETION_PASSPHRASE"))
            {
                _globalStuffService.MeatPizzas = 0;
                _globalStuffService.VeggiePizzas = 0;
                _globalStuffService.VeganPizzas = 0;
                _globalStuffService.TotalCost = 0;

                _context.Requests.RemoveRange(_context.Requests);
                _context.Results.RemoveRange(_context.Results);
                _context.SaveChanges();
                return true;
            }
            return false;
        }

        public void MarkAsPaid(int id)
        {
            _context.Results.Find(id).hasPaid = true;
            _context.SaveChanges();
        }
        public void MarkAsNotPaid(int id)
        {
            _context.Results.Find(id).hasPaid = false;
            _context.SaveChanges();
        }
    }
}
