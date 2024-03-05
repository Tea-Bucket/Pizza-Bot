using PizzaBot.Models;

namespace PizzaBot.Services
{
    public class GlobalStuffService
    {
        public string Message { get { return _message; }}
        private string _message = " ";

        public bool OrdersLocked { get { return _ordersLocked; } }
        private bool _ordersLocked;
        public readonly string LOCKED_ORDERS_MESSAGE = "Orders have been locked. Pizza will be ordered soon.";

        public event EventHandler OnLockOrMessageChange;
        public int MeatPizzas { get; set; }
        public int VeggiePizzas { get; set; }
        public int VeganPizzas { get; set; }

        public float TotalCost { get; set; }

        private PizzaConfig _pizzaConfig;
        private JSONService _jsonService;


        public bool ShouldBalance { get; set; } = true;

        public GlobalStuffService(JSONService jSONService) 
        {
            _jsonService = jSONService;
            _pizzaConfig = _jsonService.ReadPizzaConfig();
        }

        public void SetMessage(string message)
        {
            _message = message;
            OnLockOrMessageChange(this, null);
        }

        public void SetOrdersLocked(bool ordersLocked)
        {
            _ordersLocked = ordersLocked;

            ShouldBalance = true;
            OnLockOrMessageChange(this, null);
        }

        public PizzaConfig? GetConfig()
        {
            if(_pizzaConfig == null)
            {
                _pizzaConfig = _jsonService.ReadPizzaConfig();
                if(_pizzaConfig == null)
                {
                    return null;
                }
            }
            return _pizzaConfig.GetShallowCopy();
        }

        public void SetConfig(PizzaConfig pizzaConfig)
        {
            _pizzaConfig = pizzaConfig;
            _jsonService.WritePizzaConfig(pizzaConfig);
        }
    
        public float GetSizeOfSliceInCM2()
        {
            return ((_pizzaConfig.SizeX*100) * (_pizzaConfig.SizeY*100)) / _pizzaConfig.Fragments;
        }
    }
}
