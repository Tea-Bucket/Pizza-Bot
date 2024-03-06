using PizzaBot.Models;
using System.Text.Json;

namespace PizzaBot.Services
{
    public class JSONService
    {
        public IWebHostEnvironment WebHostEnvironment { get; }

        const string CONFIG_DIRECTORY_PATH = "config";
        const string PIZZA_CONFIG_FILENAME = "pizza.config";

        const string LOG_DIRECTORY_PATH = "logs";
        const string PIZZA_LOG_FILENAME = "pizza.log";

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

        public List<PizzaArchiveEntry>? GetPizzaArchive()
        {
            return ReadJson<List<PizzaArchiveEntry>>(Path.Combine(LOG_DIRECTORY_PATH, PIZZA_LOG_FILENAME));
        }

        public void WriteNewPizzaArchive(List<PizzaArchiveEntry> logs)
        {
            string path = Path.Combine(WebHostEnvironment.WebRootPath, LOG_DIRECTORY_PATH, PIZZA_LOG_FILENAME);
            if (File.Exists(path))
            {
                string newPath = Path.Combine(WebHostEnvironment.WebRootPath, LOG_DIRECTORY_PATH, (DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss") + "." + PIZZA_LOG_FILENAME));
                File.Move(path,newPath);
            }
            WriteJson(logs, Path.Combine(LOG_DIRECTORY_PATH, PIZZA_LOG_FILENAME));
        }


        private T? ReadJson<T>(string localpath) where T : class 
        {
            string path = Path.Combine(WebHostEnvironment.WebRootPath, localpath);
            if(!File.Exists(path))
            {
                return null;
            }
            string jsonString = File.ReadAllText(path);
            return JsonSerializer.Deserialize<T>(jsonString);
        }

        private void WriteJson<T>(T toSerialize,  string localpath)
        {
            string path = Path.Combine(WebHostEnvironment.WebRootPath ,localpath);
            string jsonString = JsonSerializer.Serialize<T>(toSerialize);
            File.WriteAllText(path, jsonString);
        }
    }
}
