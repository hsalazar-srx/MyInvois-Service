select  f.ESCONO,

    f.ESDIVI,

    f.ESCUNO,

    f.ESTRCD AS TransCode,

    f.ESRGDT As InvoiceEntryDate,

    f.ESCHNO As ChangeVersion,

    f.ESCINO As InvoiceNumber,

    f.ESCUCD AS CurrencyCode,

    f.escuno AS CustomerNumber,

    o.okstat AS Status,

    o.OKCUNM  AS CustomerMasterName,

    o.OKCUA1  AS MasterAddress1,

    o.OKCUA2  AS MasterAddress2,

    o.OKCUA3  AS MasterAddress3,

    o.OKCUA4  AS MasterAddress4,

    a.OPCUNM AS Name,

a.OPCUA1  AS Address1,

    a.OPCUA2  AS Address2,

    a.OPCUA3  AS Address3,

    a.OPCUA4  AS Address4,

a.OPPONO AS PostCode

 

FROM mvxcdta.FSLEDG f

JOIN mvxcdta.OCUSMA o

  ON f.ESCONO = o.OKCONO

AND f.ESCUNO = o.OKCUNO

LEFT JOIN mvxcdta.OCUSAD a

  ON a.OPCONO = f.ESCONO

AND a.OPCUNO = f.ESCUNO

AND a.OPADID = 'INV01'

WHERE f.escono = '100' and o.okcono = '100' and a.opcono = '100' and f.esdivi = 'L' and f.estrcd = '10' and o.okstat = '20' and f.esyea4 > 2025