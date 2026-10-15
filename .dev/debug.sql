SELECT "Name", "ContainerId" IS NOT NULL AS has_container, "ChannelId" IS NOT NULL AS in_channel, "FolderId", "DeletedAt" FROM files;
SELECT "Name" FROM files WHERE "Name" ILIKE '%100\%%';
SHOW standard_conforming_strings;
