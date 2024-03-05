CREATE TABLE PizzaBotTest.Requests (
Id INT PRIMARY KEY,
Name LONGTEXT NOT NULL,
reqPiecesMeat INT NOT NULL,
reqPiecesVegetarian INT NOT NULL,
reqPiecesVegan INT NOT NULL,
priority FLOAT);

CREATE TABLE PizzaBotTest.Results (
Id INT PRIMARY KEY,
resPiecesMeat INT NOT NULL,
resPiecesVegetarian INT NOT NULL,
resPiecesVegan INT NOT NULL,
penaltyMeatVeggi FLOAT NOT NULL,
penaltyVeggieVegan FLOAT NOT NULL,
totalCost FLOAT NOT NULL,
hasPaid TINYINT NOT NULL);
