using System.ComponentModel.DataAnnotations;

namespace PizzaBot.Models
{
    public enum PenaltyType
    {
        Tuxic = 0,
        PfeifferTreimer = 1,
        PfeifferTreimerLockedDown = 2,
        Shape = 3,
        PfeifferTreimerModified = 4,
    }

    public class PizzaConfig
    {
        /// <summary>
        /// size of pizza in m
        /// </summary>
        [Range(0.2f, 10)]
        public float SizeX { get; set; }
        [Range(0.2f, 10)]
        public float SizeY { get; set; }

        /// <summary>
        /// price of a pizza in ct
        /// </summary>
        [Range(1000, 10000, ErrorMessage = "Price of a Pizza needs to be between 10€ and 100€")]
        public int Price { get; set; }

        /// <summary>
        /// number of pieces per pizza
        /// </summary>
        [Range(8, 100, ErrorMessage = "Pieces per Pizza need to be between 8 and 100")]
        public int Fragments { get; set; }

        /// <summary>
        /// amount of toppings a pizza can have
        /// </summary>
        [Range(1, 50, ErrorMessage = "Toppings per Pizza need to be between  1 and 50")]
        public int Toppings { get; set; }

        /// <summary>
        /// max length of order names
        /// </summary>
        [Range(1, 500, ErrorMessage = "Name length needs to be between 1 and 500")]
        public int NameLength { get; set; }

        public PenaltyType PenaltyType { get; set; }

        public PizzaConfig GetShallowCopy()
        {
            return (PizzaConfig)this.MemberwiseClone();
        }
    }
}
