using PizzaBot.Models;
using System.Text.Json;

namespace PizzaBot.Services
{
    public class JSONService
    {
        public IWebHostEnvironment WebHostEnvironment { get; }

        const string CONFIG_DIRECTORY_PATH = "config";
        const string PIZZA_CONFIG_FILENAME = "pizza.config";

        public JSONService(IWebHostEnvironment webHostEnvironment) 
        { 
            WebHostEnvironment = webHostEnvironment;
        }

        public PizzaConfig? ReadPizzaConfig()
        {
            string path = Path.Combine(WebHostEnvironment.WebRootPath, CONFIG_DIRECTORY_PATH, PIZZA_CONFIG_FILENAME);
            string jsonString = File.ReadAllText(path);
            PizzaConfig? pizzaConfig = JsonSerializer.Deserialize<PizzaConfig>(jsonString);
            return pizzaConfig;
        }

        public void WritePizzaConfig(PizzaConfig pizzaConfig)
        {
            string path = Path.Combine(WebHostEnvironment.WebRootPath, CONFIG_DIRECTORY_PATH, PIZZA_CONFIG_FILENAME);
            string jsonString = JsonSerializer.Serialize(pizzaConfig);
            File.WriteAllText(path, jsonString);
        }

        public string GetPizzaConfigPath()
        {
            return Path.Combine(WebHostEnvironment.WebRootPath, CONFIG_DIRECTORY_PATH, PIZZA_CONFIG_FILENAME);
        }
    }
}
