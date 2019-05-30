use test_db
truncate table #test
Create table #test(id int)
insert into #test values (1)
insert into #test values (2)
insert into #test values (4)
insert into #test values (5)
insert into #test values (6)
insert into #test values (9)
insert into #test values (10)
insert into #test values (11)



With CTE AS(
SELECT SeqID AS MissingSeqID,(select max(id)  from #test)as MaxValue
FROM (SELECT ROW_NUMBER() OVER (ORDER BY id) SeqID from #test) LkUp
LEFT JOIN #test t ON t.ID = LkUp.SeqID
WHERE t.ID is null 
)

select (STUFF((SELECT ', ' + cast(MissingSeqID as varchar(max)) FROM cte FOR XML PATH('')), 1, 2, '')),maxvalue,count(maxvalue) from CTE
group by maxvalue




