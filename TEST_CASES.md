# Cas de test CPCREDO

Jeu de tests manuels (français) pour la caisse populaire. Montants affichés à 2 décimales.

## Préparation

- API et PostgreSQL démarrés.
- Connexion `caissier` / `Caissier@Cpcredo2026` pour la caisse.
- Membre démo : Marie-Claire Jean (sociétaire fondatrice).

## Caisse

| # | Cas | Résultat attendu |
|---|---|---|
| C1 | Ouvrir la caisse sans espèces en main | Refus : champ obligatoire |
| C2 | Ouvrir avec un fond saisi | Caisse ouverte ; solde théorique = fond |
| C3 | Dépôt 2 000,00 G puis 1 000,00 G | Deux journaux équilibrés ; solde épargne et caisse augmentent |
| C4 | Retrait 500,00 G | Journal Dr épargne / Cr caisse |
| C5 | Retrait > disponible | Refus `teller.insufficient` |
| C6 | Dépôt sans `Idempotency-Key` | HTTP 400 `idempotency.missing` |
| C7 | Même `Idempotency-Key` + même corps | Rejeu, pas de second journal |
| C8 | Fermer sans solde compté | Refus ; le théorique n’est pas recopié |
| C9 | Fermer avec écart et sans note | Refus |
| C10 | Fermer avec écart et note | Écart au P&L ; statut Closed |

## Mouvement interne

| # | Cas | Résultat attendu |
|---|---|---|
| M1 | Coffre → Caisse, montant saisi | En attente ; Acceptation Dr caisse / Cr coffre |
| M2 | Caisse A → Caisse A | Refus `internal.same_till` |
| M3 | Caisse A → Caisse B (B fermée) | Refus : destination doit être ouverte |

## Crédit CT90

| # | Cas | Résultat attendu |
|---|---|---|
| P1 | Usager + CT90 | Refus : CT90 interdit aux usagers |
| P2 | Décaissement sans caisse ouverte | Refus `till.not_open` |
| P3 | Décaissement 10 000,00 G, 10 % épargne | Dr 12 10 ; Cr caisse 9 000,00 ; Cr épargne 1 000,00 + gage |
| P4 | Remboursement 1 000,00 G | Allocation pénalité → intérêt → capital |
| P5 | Renouvellement DPD = 8 | Refus `loan.dpd` |
| P6 | Renouvellement DPD ≤ 7, sans pénalité | Ancien Renewed ; nouveau cycle+1 Approuvé |
| P7 | Evergreen | Cycle ≥ 3 et capital ≥ 80 % du capital original du cycle précédent |

## Rapports

| # | Cas | Résultat attendu |
|---|---|---|
| R1 | Feuille de recouvrement PDF | En-tête CPCREDO, pied de page institutionnel, 2 décimales |
| R2 | PAR 1/7/30 CT90 | Encours à DPD ≥ 1, ≥ 7, ≥ 30 / portefeuille CT90 |
| R3 | Registre des renouvellements | Ancien n°, nouveau n°, cycle, evergreen |

## Comptabilité

| # | Cas | Résultat attendu |
|---|---|---|
| J1 | PUT/PATCH/DELETE d’une écriture | 405, `journal.immutable` |
| J2 | Modifier une écriture en base | `PostedJournalImmutableException` |
| J3 | Contre-passation | Nouvelle écriture inverse, originale intacte |

## Identité

| # | Cas | Résultat attendu |
|---|---|---|
| I1 | Relance du seed | Classes de membres et drapeaux fondateurs inchangés |
| I2 | Relance du seed | Journée démo non dupliquée |
