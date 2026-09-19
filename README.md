# CPCREDO

Caisse Populaire Épargne et de Crédit pour le Développement de l’Ouest — Pétion-Ville, Haïti.

Portail du personnel (caisse, épargne, crédit CT90, trésorerie, rapports).

## Démarrage local

1. PostgreSQL 16 : base `cpcredo`, utilisateur `cpcredo`, mot de passe `cpcredo` (voir `docker-compose.yml`).
2. API :

```
dotnet run --project src/CPCREDO.WebApi
```

Écoute `http://localhost:5080`. Les migrations et le seed s’exécutent au démarrage.

3. Interface :

```
cd src/CPECDO.Web
npm install
npm run dev
```

UI : `http://localhost:5173` (proxy vers l’API).

Comptes de démonstration : `admin` / `Admin@Cpcredo2026`, `gerant` / `Gerant@Cpcredo2026`, `caissier` / `Caissier@Cpcredo2026`.

Le seed de journée démo (idempotent) ouvre une caisse, deux dépôts, un retrait, un décaissement CT90 et un remboursement.

## Sauvegarde nocturne PostgreSQL

Prévoir un `pg_dump` chaque nuit (cron, Task Scheduler ou job conteneur). Exemple Linux :

```
0 2 * * * pg_dump -h localhost -U cpcredo -d cpcredo -Fc -f /var/backups/cpcredo/cpcredo-$(date +\%Y\%m\%d).dump
```

Windows (Planificateur de tâches, 02:00) :

```
pg_dump -h localhost -U cpcredo -d cpcredo -Fc -f C:\backups\cpcredo\cpcredo-%DATE:~6,4%%DATE:~3,2%%DATE:~0,2%.dump
```

Conserver au moins 7 copies. Restauration : `pg_restore -h localhost -U cpcredo -d cpcredo --clean fichier.dump`.

Les montants s’affichent à 2 décimales ; le stockage reste `numeric(19,4)`. Les écritures validées sont immuables (contre-passation uniquement). Les POST monétaires exigent l’en-tête `Idempotency-Key`.

## Installation USB (production)

Depuis la racine du dépôt :

```
powershell -ExecutionPolicy Bypass -File installer\publish.ps1
```

Le script demande un mot de passe d’installation (il n’est jamais écrit dans git, le README ou appsettings ; seul un hash SHA-256 va dans `CPCREDO-USB/Templates/install.lock`). Copiez le dossier `CPCREDO-USB` sur la clé. Sur le serveur : clic droit `INSTALLER-SERVEUR.bat` → Exécuter en tant qu’administrateur. Sur les autres PC : double-cliquer `INSTALLER-CLIENT.bat`. Windows PowerShell 5.1 suffit (pas PowerShell 7). En production le site LAN est `https://IP:5443` (certificat auto-signé dans `C:\CPCREDO\certs\` ; la première visite du navigateur peut afficher un avertissement). `http://127.0.0.1:5080` reste local au serveur. `Seed:Enabled` est `false` : pas de fondateurs ni de journée démo. Le premier administrateur reçoit un mot de passe unique à changer à la première connexion.
